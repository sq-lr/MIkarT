using System.IO;
using MarioKart.AI;
using MarioKart.AssetsSystem;
using MarioKart.Audio;
using MarioKart.CameraSystem;
using MarioKart.Core;
using MarioKart.InputSystem;
using MarioKart.Players;
using MarioKart.Racing;
using MarioKart.Rendering;
using MarioKart.UI;
using MarioKart.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MarioKart.EditorTools
{
    /// <summary>
    /// One-click replacement for the manual scene checklist in
    /// docs/development.md. Builds Assets/Scenes/Main.unity from scratch:
    /// every GameObject, component, and [SerializeField] reference the
    /// runtime scripts expect, plus the two kart materials.
    ///
    /// This exists because scenes can't be hand-authored outside the Editor
    /// (docs/decisions/0005) -- so instead the Editor authors it for us,
    /// reproducibly. Re-running it wipes and rebuilds the scene.
    /// </summary>
    public static class MainSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string MaterialsFolder = "Assets/Materials";

        // Must cover the largest default loop (backend mock: 800 m ≈ 127 m
        // radius) plus EnvironmentGenerator's horizon layer (up to 150 m
        // beyond the loop): a 10 m Unity plane × 60 = 600 m, ±300 m.
        private const float GroundScale = 60f;

        private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        [MenuItem("MarioKart/Build Main Scene")]
        public static void Build()
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Build Main Scene", "Exit Play Mode first.", "OK");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Scene scene = OpenOrCreateScene();

            var existing = scene.GetRootGameObjects();
            if (existing.Length > 0)
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "Build Main Scene",
                    $"This rebuilds {ScenePath} from scratch and deletes its {existing.Length} existing root object(s).\n\nContinue?",
                    "Rebuild", "Cancel");
                if (!proceed) return;

                foreach (var go in existing) Object.DestroyImmediate(go);
            }

            var font = GameFonts.Display; // comic lettering; GameFonts.Body is used for typed text
            EnsureMaterials(out var kartRed, out var kartBlue, out var groundMat);

            // ---- Lighting ----------------------------------------------------
            var sun = new GameObject("Directional Light", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Hard; // crisp ink-edged shadows; ToonStyle.ConfigureShadows re-applies at runtime
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // ---- GameManager + backend clients --------------------------------
            var gameManager = new GameObject("GameManager", typeof(GameManager)).GetComponent<GameManager>();
            var music = gameManager.gameObject.AddComponent<MusicPlayer>(); // mood soundtrack; builds its own AudioSource
            var backend = new GameObject("Backend", typeof(WorldRecipeClient), typeof(MeshAssetClient));
            backend.transform.SetParent(gameManager.transform);
            var recipeClient = backend.GetComponent<WorldRecipeClient>();
            var meshClient = backend.GetComponent<MeshAssetClient>();

            // ---- World -----------------------------------------------------
            var worldGenerator = new GameObject("WorldGenerator", typeof(WorldGenerator)).GetComponent<WorldGenerator>();

            // EnvironmentGenerator and GeneratedMeshLoader share a GameObject:
            // Generate() destroys its children, so the loader can't be one.
            var envGo = new GameObject("EnvironmentGenerator", typeof(EnvironmentGenerator), typeof(GeneratedMeshLoader));
            envGo.transform.SetParent(worldGenerator.transform);
            var environmentGenerator = envGo.GetComponent<EnvironmentGenerator>();
            var meshLoader = envGo.GetComponent<GeneratedMeshLoader>();

            // TrackMeshBuilder fills this with the road + barrier meshes at runtime.
            var trackVisual = new GameObject("TrackVisual");
            trackVisual.transform.SetParent(worldGenerator.transform);

            var checkpoints = new GameObject("Checkpoints");
            checkpoints.transform.SetParent(worldGenerator.transform);

            var obstacles = new GameObject("Obstacles", typeof(ObstacleGenerator));
            obstacles.transform.SetParent(worldGenerator.transform);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(worldGenerator.transform);
            ground.transform.position = new Vector3(0f, -0.05f, 0f); // below the track line, no z-fighting
            ground.transform.localScale = new Vector3(GroundScale, 1f, GroundScale);
            var groundRenderer = ground.GetComponent<Renderer>();
            groundRenderer.sharedMaterial = groundMat;
            groundRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // receive only; casting = self-shadow acne

            // ---- Karts -----------------------------------------------------
            var kart1 = BuildKart("Kart_P1", 1, kartRed, new Vector3(-2f, 0.35f, 0f));
            var kart2 = BuildKart("Kart_P2", 2, kartBlue, new Vector3(2f, 0.35f, 0f));

            // ---- Cameras (top / bottom split) -----------------------------------
            var cam1 = BuildCamera("Camera_P1", 1, kart1.transform, withAudioListener: true);
            var cam2 = BuildCamera("Camera_P2", 2, kart2.transform, withAudioListener: false);

            // ---- Race ------------------------------------------------------
            var raceManager = new GameObject("RaceManager", typeof(RaceManager)).GetComponent<RaceManager>();
            var bootstrap = new GameObject("RaceBootstrap", typeof(RaceBootstrap)).GetComponent<RaceBootstrap>();

            // ---- UI --------------------------------------------------------
            var canvas = BuildCanvas();
            BuildLobbyPanel(canvas, font);
            BuildUploadPanel(canvas, font);
            BuildGeneratingPanel(canvas, font);
            BuildHudPanel(canvas, font, kart1.GetComponent<LapManager>(), kart2.GetComponent<LapManager>());
            var results = BuildResultsPanel(canvas, font);

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            // ---- Wiring (private [SerializeField]s, via SerializedObject) -------
            Set(gameManager, "recipeClient", recipeClient);
            Set(gameManager, "meshAssetClient", meshClient);
            Set(gameManager, "worldGenerator", worldGenerator);
            Set(music, "worldGenerator", worldGenerator);
            Set(gameManager, "raceManager", raceManager);
            Set(gameManager, "resultsUI", results);

            Set(worldGenerator, "environmentGenerator", environmentGenerator);
            Set(worldGenerator, "trackVisualRoot", trackVisual.transform);
            Set(worldGenerator, "checkpointRoot", checkpoints.transform);
            Set(worldGenerator, "obstacleRoot", obstacles.transform);
            Set(worldGenerator, "sunLight", sun);
            Set(worldGenerator, "groundRenderer", groundRenderer);

            Set(environmentGenerator, "meshLoader", meshLoader);
            Set(meshLoader, "client", meshClient);

            SetArray(raceManager, "players", kart1.GetComponent<LapManager>(), kart2.GetComponent<LapManager>());

            Set(bootstrap, "worldGenerator", worldGenerator);
            Set(bootstrap, "raceManager", raceManager);
            SetArray(bootstrap, "karts", kart1.GetComponent<Rigidbody>(), kart2.GetComponent<Rigidbody>());
            SetArray(bootstrap, "cameras", cam1, cam2);

            // ---- Save ------------------------------------------------------
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            Selection.activeGameObject = gameManager.gameObject;
            Debug.Log($"MainSceneBuilder: built and saved {ScenePath} ({scene.rootCount} root objects). " +
                      "Press Play: Lobby → Start → Choose Image → Generate.");
        }

        // ------------------------------------------------------------------
        // Scene / assets
        // ------------------------------------------------------------------

        private static Scene OpenOrCreateScene()
        {
            if (File.Exists(ScenePath))
            {
                return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            return scene;
        }

        /// <summary>
        /// Re-point the kart and ground material assets at the toon shaders
        /// (docs/decisions/0008) without rebuilding the scene. Build Main
        /// Scene does this too; this is for applying a shader change to an
        /// existing scene.
        /// </summary>
        [MenuItem("MarioKart/Apply Toon Materials")]
        public static void ApplyToonMaterials()
        {
            EnsureMaterials(out _, out _, out _);
            AssetDatabase.SaveAssets();
            Debug.Log("MainSceneBuilder: kart and ground materials now use the MarioKart/Toon shaders.");
        }

        /// <summary>
        /// The three material assets the scene references. Karts get the
        /// outlined toon shader with a full-strength rim so they stay
        /// readable at split-screen size; the ground gets the outline-free
        /// variant. Falls back to Standard if the shaders fail to compile.
        /// </summary>
        private static void EnsureMaterials(out Material kartRed, out Material kartBlue, out Material ground)
        {
            var toon = Shader.Find("MarioKart/Toon") ?? Shader.Find("Standard");
            var toonFlat = Shader.Find("MarioKart/Toon (No Outline)") ?? Shader.Find("Standard");

            // Karts: full rim and a hard specular dot (glossy toy); the
            // environment gets neither.
            kartRed = EnsureMaterial("Kart_P1", new Color(0.85f, 0.2f, 0.2f), toon, rimStrength: 1f, specStrength: 1f);
            kartBlue = EnsureMaterial("Kart_P2", new Color(0.2f, 0.4f, 0.9f), toon, rimStrength: 1f, specStrength: 1f);
            ground = EnsureMaterial("Ground", new Color(0.35f, 0.55f, 0.3f), toonFlat, rimStrength: 0f, specStrength: 0f);
        }

        /// <summary>
        /// Materials must be real assets (not scene-embedded objects) or
        /// they're dropped on save. Reuses an existing one so re-running the
        /// builder doesn't churn GUIDs; the shader is reassigned every time
        /// so an existing asset picks up shader changes.
        /// </summary>
        private static Material EnsureMaterial(string name, Color color, Shader shader, float rimStrength, float specStrength)
        {
            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Materials");
            }

            string path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.color = color;
            if (material.HasProperty("_RimStrength")) material.SetFloat("_RimStrength", rimStrength);
            if (material.HasProperty("_SpecStrength")) material.SetFloat("_SpecStrength", specStrength);
            EditorUtility.SetDirty(material);
            return material;
        }

        // ------------------------------------------------------------------
        // Gameplay objects
        // ------------------------------------------------------------------

        private static GameObject BuildKart(string name, int playerIndex, Material material, Vector3 position)
        {
            // The cube is the physics body (BoxCollider) and carries the
            // player-colour material; KartVisual hides it at runtime and
            // builds the go-kart shape on top of it.
            var kart = GameObject.CreatePrimitive(PrimitiveType.Cube);
            kart.name = name;
            kart.transform.position = position;
            kart.transform.localScale = new Vector3(1.6f, 0.6f, 2.6f);
            kart.GetComponent<Renderer>().sharedMaterial = material;

            var rb = kart.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            var controller = kart.AddComponent<KartController>();
            var input = kart.AddComponent<PlayerInput>();
            var player = kart.AddComponent<PlayerController>();
            var laps = kart.AddComponent<LapManager>();
            laps.playerIndex = playerIndex;
            kart.AddComponent<KartVisual>();      // builds the wheels/body/driver meshes at runtime
            kart.AddComponent<KartSpeedEffect>(); // builds its own particle systems at runtime
            kart.AddComponent<KartSkidEffect>();  // likewise: smoke, sparks, skid marks
            var audio = kart.AddComponent<KartAudio>(); // engine loops + one-shots from Resources/Audio
            audio.pitchOffset = playerIndex == 2 ? 0.04f : -0.04f; // the two engines shouldn't phase

            Set(controller, "rb", rb);
            Set(player, "input", input);
            Set(player, "kart", controller);

            var bindings = playerIndex == 2 ? PlayerInputConfig.ArrowKeys() : PlayerInputConfig.WASD();
            var so = new SerializedObject(input);
            so.FindProperty("playerIndex").intValue = playerIndex;
            so.FindProperty("config.accelerate").intValue = (int)bindings.accelerate;
            so.FindProperty("config.brake").intValue = (int)bindings.brake;
            so.FindProperty("config.left").intValue = (int)bindings.left;
            so.FindProperty("config.right").intValue = (int)bindings.right;
            so.ApplyModifiedPropertiesWithoutUndo();

            return kart;
        }

        private static PlayerCamera BuildCamera(string name, int playerIndex, Transform target, bool withAudioListener)
        {
            var go = new GameObject(name, typeof(Camera), typeof(PlayerCamera), typeof(PaperGrainEffect), typeof(SpeedLinesEffect));
            if (withAudioListener) go.AddComponent<AudioListener>(); // exactly one per scene

            var cam = go.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.rect = playerIndex == 1 ? new Rect(0f, 0.5f, 1f, 0.5f) : new Rect(0f, 0f, 1f, 0.5f);
            cam.depth = playerIndex;

            // Chase cam tuned in the Editor: 2.15 m above and 6.2 m behind the
            // kart, pitched 12.47° down. Same for both players.
            var follow = go.GetComponent<PlayerCamera>();
            follow.offset = new Vector3(0f, 2.15f, -6.2f);
            follow.pitchDegrees = 12.47f;
            follow.yawDegrees = 0f;
            Set(follow, "cam", cam);
            Set(follow, "target", target);
            follow.SnapToTarget();
            return follow;
        }

        // ------------------------------------------------------------------
        // UI (legacy UnityEngine.UI, matching the UI/*.cs scripts)
        // ------------------------------------------------------------------

        private static Canvas BuildCanvas()
        {
            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        private static LobbyUI BuildLobbyPanel(Canvas canvas, Font font)
        {
            var panel = CreatePanel(canvas.transform, "LobbyUI", dim: true, out var holder);
            CreateText(panel.transform, "Title", "MARIO KART: AI WORLDS", font, 64, Center, new Vector2(0f, 120f), new Vector2(1200f, 100f), TextAnchor.MiddleCenter);
            CreateText(panel.transform, "Hint", "P1: WASD    P2: Arrow keys", GameFonts.Body, 28, Center, new Vector2(0f, 40f), new Vector2(800f, 40f), TextAnchor.MiddleCenter);
            var start = CreateButton(panel.transform, "StartButton", "Start", font, new Vector2(0f, -60f), new Vector2(320f, 80f));

            var ui = holder.AddComponent<LobbyUI>();
            Set(ui, "panel", panel);
            Set(ui, "startButton", start);
            return ui;
        }

        private static ImageUploadUI BuildUploadPanel(Canvas canvas, Font font)
        {
            var panel = CreatePanel(canvas.transform, "ImageUploadUI", dim: true, out var holder);
            CreateText(panel.transform, "Title", "Describe your world", font, 48, Center, new Vector2(0f, 360f), new Vector2(1200f, 70f), TextAnchor.MiddleCenter);

            var preview = new GameObject("Preview", typeof(RawImage)).GetComponent<RawImage>();
            preview.transform.SetParent(panel.transform, false);
            preview.color = new Color(1f, 1f, 1f, 0.9f);
            SetRect(preview.rectTransform, Center, new Vector2(0f, 120f), new Vector2(480f, 360f));

            var choose = CreateButton(panel.transform, "ChooseImageButton", "Choose Image", font, new Vector2(0f, -110f), new Vector2(320f, 70f));
            // Typed text gets the mixed-case body font; Bangers is all caps.
            var description = CreateInputField(panel.transform, "DescriptionField", "One sentence about the scene in the photo...", GameFonts.Body, new Vector2(0f, -210f), new Vector2(900f, 70f));
            CreateText(panel.transform, "SurfaceHint", "Track surface is selected from the uploaded image", font, 22, Center, new Vector2(0f, -285f), new Vector2(900f, 36f), TextAnchor.MiddleCenter);
            var skyGroup = new GameObject("SkyOptions", typeof(RectTransform), typeof(ToggleGroup)).GetComponent<ToggleGroup>();
            skyGroup.transform.SetParent(panel.transform, false);
            SetRect(skyGroup.GetComponent<RectTransform>(), Center, new Vector2(0f, -350f), new Vector2(760f, 58f));
            var sunny = CreateSkyToggle(skyGroup.transform, "Sunny", "Sunny Sky", font, new Vector2(-285f, 0f));
            var cloudy = CreateSkyToggle(skyGroup.transform, "Cloudy", "Cloudy Sky", font, new Vector2(-95f, 0f));
            var sunset = CreateSkyToggle(skyGroup.transform, "Sunset", "Sunset Sky", font, new Vector2(95f, 0f));
            var night = CreateSkyToggle(skyGroup.transform, "Night", "Night Sky", font, new Vector2(285f, 0f));
            var personalize = CreatePersonalizeToggle(panel.transform, font, new Vector2(0f, -410f));
            var generate = CreateButton(panel.transform, "GenerateButton", "Generate World", font, new Vector2(0f, -310f), new Vector2(320f, 80f));
            generate.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -470f);

            var ui = holder.AddComponent<ImageUploadUI>();
            Set(ui, "panel", panel);
            Set(ui, "chooseImageButton", choose);
            Set(ui, "previewImage", preview);
            Set(ui, "descriptionField", description);
            Set(ui, "sunnySkyToggle", sunny);
            Set(ui, "cloudySkyToggle", cloudy);
            Set(ui, "sunsetSkyToggle", sunset);
            Set(ui, "nightSkyToggle", night);
            Set(ui, "skyToggleGroup", skyGroup);
            Set(ui, "personalizeToggle", personalize);
            Set(ui, "generateButton", generate);
            return ui;
        }

        private static Toggle CreatePersonalizeToggle(Transform parent, Font font, Vector2 position)
        {
            // Off by default: fast/free retrieval-only world unless the
            // player opts into slower, personalized Meshy generation. See
            // docs/decisions/0012-personalize-toggle.md.
            var go = new GameObject("PersonalizeToggle", typeof(RectTransform), typeof(Image), typeof(Toggle));
            go.transform.SetParent(parent, false);
            SetRect(go.GetComponent<RectTransform>(), Center, position, new Vector2(340f, 42f));

            var background = go.GetComponent<Image>();
            background.color = new Color(0.16f, 0.16f, 0.16f, 0.95f);
            var toggle = go.GetComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.isOn = false;
            toggle.colors = new ColorBlock
            {
                normalColor = new Color(0.16f, 0.16f, 0.16f),
                highlightedColor = new Color(0.30f, 0.50f, 0.80f),
                pressedColor = new Color(0.20f, 0.40f, 0.70f),
                selectedColor = new Color(0.25f, 0.65f, 0.35f),
                disabledColor = new Color(0.10f, 0.10f, 0.10f),
                colorMultiplier = 1f,
                fadeDuration = 0.1f,
            };

            var check = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            check.transform.SetParent(go.transform, false);
            var checkRect = check.GetComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0f, 0.5f);
            checkRect.anchorMax = new Vector2(0f, 0.5f);
            checkRect.pivot = new Vector2(0f, 0.5f);
            checkRect.anchoredPosition = new Vector2(8f, 0f);
            checkRect.sizeDelta = new Vector2(26f, 26f);
            check.GetComponent<Image>().color = new Color(0.55f, 0.72f, 0.20f);
            toggle.graphic = check.GetComponent<Image>();

            var text = CreateText(go.transform, "Label", "Generate personalized assets", font, 20, Center, Vector2.zero, new Vector2(340f, 42f), TextAnchor.MiddleLeft);
            text.color = Color.white;
            var textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(42f, 0f);
            textRect.offsetMax = Vector2.zero;

            return toggle;
        }

        private static Toggle CreateSkyToggle(Transform parent, string name, string label, Font font, Vector2 position)
        {
            var go = new GameObject(name, typeof(Image), typeof(Toggle));
            go.transform.SetParent(parent, false);
            SetRect(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), position, new Vector2(170f, 52f));

            var background = go.GetComponent<Image>();
            background.color = new Color(0.08f, 0.22f, 0.19f);
            var toggle = go.GetComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.colors = new ColorBlock
            {
                normalColor = new Color(0.08f, 0.22f, 0.19f),
                highlightedColor = new Color(0.22f, 0.48f, 0.36f),
                pressedColor = new Color(0.15f, 0.38f, 0.28f),
                selectedColor = new Color(0.55f, 0.72f, 0.20f),
                disabledColor = Color.gray,
                colorMultiplier = 1f,
                fadeDuration = 0.1f,
            };

            var text = CreateText(go.transform, "Label", label, font, 22, Center, Vector2.zero, new Vector2(170f, 52f), TextAnchor.MiddleCenter);
            text.color = Color.white;
            Stretch(text.rectTransform);
            return toggle;
        }

        private static GenerationUI BuildGeneratingPanel(Canvas canvas, Font font)
        {
            // Dimmed while generating (GenerationUI drives the alpha), clear
            // during the countdown so it plays over the freshly built world.
            var panel = CreatePanel(canvas.transform, "GenerationUI", dim: true, out var holder);
            var status = CreateText(panel.transform, "Status", "Generating your world...", font, 64, Center, Vector2.zero, new Vector2(1400f, 200f), TextAnchor.MiddleCenter);

            // Outline so the countdown reads on any background once the dim clears.
            var outline = status.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            outline.effectDistance = new Vector2(3f, -3f);

            var ui = holder.AddComponent<GenerationUI>();
            Set(ui, "panel", panel);
            Set(ui, "statusText", status);
            return ui;
        }

        private static RaceHUD BuildHudPanel(Canvas canvas, Font font, LapManager p1, LapManager p2)
        {
            // No dim overlay: the HUD sits on top of the live race.
            var panel = CreatePanel(canvas.transform, "RaceHUD", dim: false, out var holder);

            // Top-left of each player's half of the screen.
            var p1Text = CreateText(panel.transform, "P1Text", "P1 Lap 0", font, 36, new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(400f, 50f), TextAnchor.UpperLeft);
            var p2Text = CreateText(panel.transform, "P2Text", "P2 Lap 0", font, 36, new Vector2(0f, 0.5f), new Vector2(24f, -24f), new Vector2(400f, 50f), TextAnchor.UpperLeft);
            p1Text.rectTransform.pivot = p2Text.rectTransform.pivot = new Vector2(0f, 1f);

            var ui = holder.AddComponent<RaceHUD>();
            Set(ui, "panel", panel);
            Set(ui, "player1Laps", p1);
            Set(ui, "player2Laps", p2);
            Set(ui, "player1Text", p1Text);
            Set(ui, "player2Text", p2Text);
            return ui;
        }

        private static ResultsUI BuildResultsPanel(Canvas canvas, Font font)
        {
            // No dim overlay: the banner stamps on top of the live race.
            var panel = CreatePanel(canvas.transform, "ResultsUI", dim: false, out var holder);
            var again = CreateButton(panel.transform, "PlayAgainButton", "PLAY AGAIN", font, new Vector2(0f, 0f), new Vector2(240f, 64f));

            var ui = holder.AddComponent<ResultsUI>();
            Set(ui, "panel", panel);
            Set(ui, "playAgainButton", again);
            return ui;
        }

        // ---- UI primitives ---------------------------------------------------

        /// <summary>
        /// Two objects, not one: the UI script lives on `holder` (always
        /// active) and toggles the `Panel` child. The UI scripts subscribe to
        /// GameManager in OnEnable and unsubscribe in OnDisable, so a script
        /// that sat on the panel it hides would deactivate itself, unsubscribe,
        /// and never hear another state change.
        /// </summary>
        private static GameObject CreatePanel(Transform canvas, string name, bool dim, out GameObject holder)
        {
            holder = new GameObject(name, typeof(RectTransform));
            holder.transform.SetParent(canvas, false);
            Stretch(holder.GetComponent<RectTransform>());

            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(holder.transform, false);
            Stretch(panel.GetComponent<RectTransform>());

            if (dim)
            {
                var image = panel.AddComponent<Image>();
                image.color = new Color(0f, 0f, 0f, 0.65f);
            }
            return panel;
        }

        private static Text CreateText(Transform parent, string name, string content, Font font, int size,
            Vector2 anchor, Vector2 position, Vector2 dimensions, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = font;
            text.fontSize = size;
            text.color = Color.white;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            SetRect(text.rectTransform, anchor, position, dimensions);
            return text;
        }

        private static Button CreateButton(Transform parent, string name, string label, Font font, Vector2 position, Vector2 dimensions)
        {
            var go = new GameObject(name, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.95f, 0.95f, 0.95f);
            SetRect(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), position, dimensions);

            var text = CreateText(go.transform, "Label", label, font, 32, Center, Vector2.zero, dimensions, TextAnchor.MiddleCenter);
            text.color = new Color(0.1f, 0.1f, 0.1f);
            Stretch(text.rectTransform);

            return go.GetComponent<Button>();
        }

        private static InputField CreateInputField(Transform parent, string name, string placeholderText, Font font, Vector2 position, Vector2 dimensions)
        {
            var go = new GameObject(name, typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = Color.white;
            SetRect(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), position, dimensions);

            var placeholder = CreateText(go.transform, "Placeholder", placeholderText, font, 28, Center, Vector2.zero, dimensions, TextAnchor.MiddleLeft);
            placeholder.color = new Color(0.5f, 0.5f, 0.5f);
            placeholder.fontStyle = FontStyle.Italic;
            Stretch(placeholder.rectTransform, padding: 12f);

            var text = CreateText(go.transform, "Text", "", font, 28, Center, Vector2.zero, dimensions, TextAnchor.MiddleLeft);
            text.color = new Color(0.1f, 0.1f, 0.1f);
            text.supportRichText = false;
            Stretch(text.rectTransform, padding: 12f);

            var field = go.GetComponent<InputField>();
            field.textComponent = text;
            field.placeholder = placeholder;
            field.lineType = InputField.LineType.SingleLine;
            return field;
        }

        private static void SetRect(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 dimensions)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;
        }

        private static void Stretch(RectTransform rect, float padding = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        // ------------------------------------------------------------------
        // [SerializeField] wiring
        // ------------------------------------------------------------------

        private static void Set(Object target, string property, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(property);
            if (prop == null)
            {
                Debug.LogError($"MainSceneBuilder: {target.GetType().Name} has no serialized field '{property}' -- script changed? Wire it by hand.", target);
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(Object target, string property, params Object[] values)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(property);
            if (prop == null)
            {
                Debug.LogError($"MainSceneBuilder: {target.GetType().Name} has no serialized field '{property}' -- script changed? Wire it by hand.", target);
                return;
            }
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
