using System;
using System.Collections.Generic;
using MarioKart.AI;
using MarioKart.Players;
using MarioKart.Racing;
using MarioKart.Rendering;
using UnityEngine;

namespace MarioKart.World
{
    /// <summary>
    /// Orchestrates world generation: derives sub-seeds from
    /// recipe.seed, builds the track, spawns checkpoints, places
    /// pass-through track obstacles (reshuffled each Generate), populates the
    /// environment, and applies the Totoro countryside look (wrap light,
    /// milky haze, dirt path, watercolor sky). This is the boundary where
    /// the WorldRecipe (data) becomes an actual scene (Unity objects).
    /// </summary>
    public class WorldGenerator : MonoBehaviour
    {
        [SerializeField] private EnvironmentGenerator environmentGenerator;
        [SerializeField] private Transform trackVisualRoot;
        [SerializeField] private Transform checkpointRoot;
        [SerializeField] private Transform obstacleRoot;
        [SerializeField] private Light sunLight;
        [SerializeField] private Renderer groundRenderer;

        private Material skyboxMaterial;

        public event Action OnWorldReady;

        public GeneratedTrack CurrentTrack { get; private set; }

        /// <summary>The recipe the current world was built from (set before OnWorldReady fires).</summary>
        public WorldRecipe CurrentRecipe { get; private set; }

        /// <summary>The generated-mesh loader, so GameManager can wait on it.</summary>
        public MarioKart.AssetsSystem.GeneratedMeshLoader MeshLoader =>
            environmentGenerator != null ? environmentGenerator.MeshLoader : null;

        public void Generate(WorldRecipe recipe)
        {
            CurrentRecipe = recipe;
            int trackSeed = WorldRandom.DeriveSeed(recipe.seed, "track");
            int envSeed = WorldRandom.DeriveSeed(recipe.seed, "environment");

            var track = new TrackGenerator().Generate(recipe, trackSeed);
            CurrentTrack = track;

            BuildTrackVisual(track, recipe.palette, recipe.track != null ? recipe.track.surface : "concrete");
            BuildCheckpoints(track);

            if (environmentGenerator != null)
            {
                environmentGenerator.Generate(recipe, track, envSeed);
            }

            var obstacles = EnsureObstacleGenerator();
            obstacles.Generate(track, recipe.seed);

            ApplyTheme(recipe);
            ApplySky(recipe.world != null ? recipe.world.sky : "sunny", recipe.palette);
            ToonStyle.ApplyPalette(recipe.palette, RenderSettings.ambientLight);

            var config = MarioKart.Core.GameManager.Instance != null ? MarioKart.Core.GameManager.Instance.Config : null;
            ToonStyle.ConfigureShadows(sunLight, config != null ? config.shadowDistance : 70f);
            if (groundRenderer != null)
            {
                // Receive only, like the road (see TrackMeshBuilder.BuildRoad).
                groundRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ToonStyle.DisableRim(groundRenderer.material);
            }

            OnWorldReady?.Invoke();
        }

        /// <summary>
        /// Dirt-path ribbon + wooden fences (see TrackMeshBuilder). Palette
        /// tints the path and moss kerbs without turning them back into asphalt.
        /// </summary>
        private void BuildTrackVisual(GeneratedTrack track, List<string> palette, string surface)
        {
            if (trackVisualRoot == null) return;

            var roadColor = Color.Lerp(GhibliLook.PathDirt, TrackMeshBuilder.SurfaceColor(surface), 0.4f);
            var wallColor = GhibliLook.FenceWood;
            var curbColor = GhibliLook.Moss;
            if (palette != null && palette.Count > 1 && ColorUtility.TryParseHtmlString(palette[1], out var ground))
            {
                roadColor = Color.Lerp(roadColor, ground, 0.2f);
                curbColor = Color.Lerp(GhibliLook.Moss, ground, 0.35f);
            }
            if (palette != null && palette.Count > 2 && ColorUtility.TryParseHtmlString(palette[2], out var tint))
            {
                wallColor = Color.Lerp(GhibliLook.FenceWood, tint, 0.25f);
            }

            // The terrain mesh shares the ground plane's material instance so
            // the embankments are indistinguishable from the plain, and the
            // palette tint ApplyPalette writes to it afterwards reaches both.
            Material groundMaterial = groundRenderer != null ? groundRenderer.material : null;

            TrackMeshBuilder.Build(trackVisualRoot, track, roadColor, wallColor, curbColor, groundMaterial);
        }

        private void BuildCheckpoints(GeneratedTrack track)
        {
            if (checkpointRoot == null) return;

            foreach (Transform child in checkpointRoot)
            {
                Destroy(child.gameObject);
            }

            for (int i = 0; i < track.checkpointPositions.Count; i++)
            {
                var go = new GameObject($"Checkpoint_{i}");
                go.transform.SetParent(checkpointRoot, worldPositionStays: false);
                go.transform.position = track.checkpointPositions[i];
                // Face the track tangent so the (width, 4, 2) box below is a
                // gate *across* the track everywhere on the loop, not just
                // where the track happens to run along the Z axis.
                go.transform.rotation = Quaternion.LookRotation(TangentAt(track, i * (track.controlPoints.Count / track.checkpointPositions.Count)), Vector3.up);

                // The control point sits on the road surface (which now has
                // hills); the gate reaches 1 m below it and 5 m above.
                var collider = go.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = new Vector3(track.width, 6f, 2f);
                collider.center = new Vector3(0f, 2f, 0f);

                var checkpoint = go.AddComponent<Checkpoint>();
                checkpoint.checkpointIndex = i;
            }
        }

        /// <summary>
        /// Runtime-created if the scene wasn't rebuilt after obstacles were
        /// added, so an already-wired Main.unity still gets pickups.
        /// </summary>
        private ObstacleGenerator EnsureObstacleGenerator()
        {
            var root = EnsureObstacleRoot();
            var generator = root.GetComponent<ObstacleGenerator>();
            if (generator == null)
            {
                generator = root.gameObject.AddComponent<ObstacleGenerator>();
            }
            return generator;
        }

        private Transform EnsureObstacleRoot()
        {
            if (obstacleRoot != null) return obstacleRoot;

            var existing = transform.Find("Obstacles");
            if (existing != null)
            {
                obstacleRoot = existing;
                return obstacleRoot;
            }

            var go = new GameObject("Obstacles");
            go.transform.SetParent(transform, worldPositionStays: false);
            obstacleRoot = go.transform;
            return obstacleRoot;
        }

        /// <summary>
        /// Direction of travel at control point `index` (central difference
        /// around the loop). Shared by checkpoints and the start-line
        /// placement in RaceBootstrap.
        /// </summary>
        public static Vector3 TangentAt(GeneratedTrack track, int index)
        {
            int count = track.controlPoints.Count;
            var prev = track.controlPoints[(index - 1 + count) % count];
            var next = track.controlPoints[(index + 1) % count];
            var tangent = next - prev;
            tangent.y = 0f;
            return tangent.sqrMagnitude > 0f ? tangent.normalized : Vector3.forward;
        }

        private void ApplyTheme(WorldRecipe recipe)
        {
            var palette = recipe?.palette;
            ColorUtility.TryParseHtmlString(palette != null && palette.Count > 0 ? palette[0] : "#F4E6C0", out var sunColor);
            ColorUtility.TryParseHtmlString(palette != null && palette.Count > 1 ? palette[1] : "#6B8F4A", out var groundColor);
            ColorUtility.TryParseHtmlString(palette != null && palette.Count > 2 ? palette[2] : "#A8C8DC", out var accent);

            groundColor = Color.Lerp(groundColor, GhibliLook.Moss, 0.45f);
            sunColor = Color.Lerp(sunColor, new Color(1f, 0.93f, 0.75f), 0.45f);

            string tod = recipe?.world?.time_of_day?.ToLowerInvariant() ?? "day";
            string weather = recipe?.world?.weather?.ToLowerInvariant() ?? "clear";

            float sunPitch = 38f;
            float sunIntensity = 0.95f;
            Color ambient = new Color(0.62f, 0.66f, 0.58f);
            Color skyTop = Color.Lerp(new Color(0.55f, 0.73f, 0.88f), accent, 0.25f);
            Color skyHorizon = GhibliLook.Cream;
            Color fog = new Color(0.78f, 0.84f, 0.78f);
            float fogDensity = 0.0075f;

            if (tod.Contains("night"))
            {
                sunPitch = 12f;
                sunIntensity = 0.35f;
                sunColor = new Color(0.55f, 0.62f, 0.85f);
                ambient = new Color(0.22f, 0.26f, 0.38f);
                skyTop = new Color(0.18f, 0.24f, 0.42f);
                skyHorizon = new Color(0.35f, 0.32f, 0.40f);
                fog = new Color(0.20f, 0.24f, 0.32f);
                fogDensity = 0.012f;
            }
            else if (tod.Contains("dusk") || tod.Contains("sunset") || tod.Contains("evening"))
            {
                sunPitch = 12f;
                sunIntensity = 0.7f;
                sunColor = new Color(1f, 0.62f, 0.38f);
                ambient = new Color(0.55f, 0.42f, 0.38f);
                skyTop = new Color(0.55f, 0.42f, 0.62f);
                skyHorizon = new Color(0.98f, 0.72f, 0.48f);
                fog = new Color(0.82f, 0.62f, 0.48f);
            }
            else if (tod.Contains("dawn") || tod.Contains("sunrise") || tod.Contains("morning"))
            {
                sunPitch = 22f;
                sunColor = new Color(1f, 0.82f, 0.62f);
                skyHorizon = new Color(0.98f, 0.86f, 0.72f);
            }

            if (weather.Contains("fog") || weather.Contains("mist") || weather.Contains("haze"))
            {
                fogDensity *= 2.4f;
            }
            else if (weather.Contains("rain") || weather.Contains("storm") || weather.Contains("cloud"))
            {
                fogDensity *= 1.5f;
                sunIntensity *= 0.75f;
                skyTop = Color.Lerp(skyTop, new Color(0.62f, 0.68f, 0.72f), 0.4f);
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fog;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;

            var skyShader = Shader.Find(GhibliLook.SkyShaderName) ?? Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                if (skyboxMaterial != null) Destroy(skyboxMaterial);
                skyboxMaterial = new Material(skyShader);
                if (skyboxMaterial.HasProperty("_SkyTop")) skyboxMaterial.SetColor("_SkyTop", skyTop);
                if (skyboxMaterial.HasProperty("_SkyHorizon")) skyboxMaterial.SetColor("_SkyHorizon", skyHorizon);
                if (skyboxMaterial.HasProperty("_SkyUpperMid"))
                {
                    skyboxMaterial.SetColor("_SkyUpperMid", sky == "sunset"
                        ? new Color(0.48f, 0.44f, 0.63f)
                        : sky == "night"
                            ? new Color(0.06f, 0.18f, 0.42f)
                            : sky == "cloudy"
                                ? new Color(0.24f, 0.52f, 0.72f)
                                : new Color(0.17f, 0.58f, 0.78f));
                }
                if (skyboxMaterial.HasProperty("_SkyLowerMid"))
                {
                    skyboxMaterial.SetColor("_SkyLowerMid", sky == "sunset"
                        ? new Color(0.82f, 0.48f, 0.44f)
                        : sky == "night"
                            ? new Color(0.10f, 0.30f, 0.58f)
                            : sky == "cloudy"
                                ? new Color(0.45f, 0.70f, 0.80f)
                                : new Color(0.36f, 0.72f, 0.84f));
                }
                if (skyboxMaterial.HasProperty("_SkyGround")) skyboxMaterial.SetColor("_SkyGround", groundColor);
                if (skyboxMaterial.HasProperty("_SkyTint")) skyboxMaterial.SetColor("_SkyTint", skyTop);
                if (skyboxMaterial.HasProperty("_GroundColor")) skyboxMaterial.SetColor("_GroundColor", groundColor);
                RenderSettings.skybox = skyboxMaterial;
                DynamicGI.UpdateEnvironment();
            }

            if (sunLight != null)
            {
                sunLight.color = sunColor;
                sunLight.intensity = sunIntensity;
                sunLight.shadows = LightShadows.Soft;
                sunLight.shadowStrength = 0.55f;
                sunLight.transform.rotation = Quaternion.Euler(sunPitch, -25f, 0f);
            }

            if (groundRenderer != null)
            {
                groundRenderer.sharedMaterial = GhibliLook.Lit(groundColor);
            }

            EnsureClouds().Rebuild(CurrentTrack, recipe != null ? recipe.seed : 1, recipe?.world?.sky ?? "sunny");
            GhibliLook.EnsurePostOnCameras();
        }

        private GhibliClouds EnsureClouds()
        {
            var existing = transform.Find("GhibliClouds");
            var go = existing != null ? existing.gameObject : new GameObject("GhibliClouds");
            go.transform.SetParent(transform, worldPositionStays: false);
            var clouds = go.GetComponent<GhibliClouds>();
            if (clouds == null) clouds = go.AddComponent<GhibliClouds>();
            return clouds;
        }

        private void OnDestroy()
        {
            if (skyboxMaterial != null) Destroy(skyboxMaterial);
        }

        /// <summary>
        /// Sky preset chosen on the upload screen (recipe.world.sky). Tints
        /// the Totoro watercolor skybox and the toon shadow/rim; sunny leaves
        /// ApplyTheme as-is. Cameras stay on the skybox so the horizon reads.
        /// </summary>
        private void ApplySky(string sky, List<string> palette)
        {
            Color skyTop;
            Color skyHorizon;
            Color ambient;
            Color sunColor;
            float sunPitch = 38f;
            float sunIntensity = 0.95f;
            switch (sky)
            {
                case "cloudy":
                    skyTop = new Color(0.42f, 0.62f, 0.78f);
                    skyHorizon = new Color(0.72f, 0.80f, 0.78f);
                    ambient = new Color(0.55f, 0.64f, 0.66f);
                    sunColor = new Color(0.85f, 0.86f, 0.88f);
                    sunIntensity = 0.7f;
                    break;
                case "sunset":
                    skyTop = new Color(0.23f, 0.35f, 0.55f);
                    skyHorizon = new Color(1.0f, 0.85f, 0.54f);
                    ambient = new Color(0.72f, 0.42f, 0.32f);
                    sunColor = new Color(1f, 0.62f, 0.38f);
                    sunPitch = 12f;
                    sunIntensity = 0.7f;
                    break;
                case "night":
                    skyTop = new Color(0.18f, 0.24f, 0.42f);
                    skyHorizon = new Color(0.35f, 0.32f, 0.40f);
                    ambient = new Color(0.18f, 0.22f, 0.32f);
                    sunColor = new Color(0.55f, 0.62f, 0.85f);
                    sunPitch = 12f;
                    sunIntensity = 0.35f;
                    break;
                default:
                    skyTop = new Color(0.086f, 0.565f, 0.788f);
                    skyHorizon = new Color(0.722f, 0.925f, 0.91f);
                    ambient = new Color(0.68f, 0.78f, 0.78f);
                    sunColor = new Color(1f, 0.97f, 0.78f);
                    sunIntensity = 1.15f;
                    break;
            }

            RenderSettings.ambientLight = ambient;
            ToonStyle.ApplyPalette(palette, ambient);
            if (skyboxMaterial != null)
            {
                if (skyboxMaterial.HasProperty("_SkyTop")) skyboxMaterial.SetColor("_SkyTop", skyTop);
                if (skyboxMaterial.HasProperty("_SkyHorizon")) skyboxMaterial.SetColor("_SkyHorizon", skyHorizon);
                if (skyboxMaterial.HasProperty("_SkyTint")) skyboxMaterial.SetColor("_SkyTint", skyTop);
                if (skyboxMaterial.HasProperty("_SkyGround")) skyboxMaterial.SetColor("_SkyGround", GhibliLook.Moss);
                if (skyboxMaterial.HasProperty("_SunColor")) skyboxMaterial.SetColor("_SunColor", sunColor);
                if (skyboxMaterial.HasProperty("_SunSize")) skyboxMaterial.SetFloat("_SunSize", sky == "sunset" ? 0.11f : sky == "night" ? 0.01f : 0.035f);
                if (skyboxMaterial.HasProperty("_SunDirection"))
                {
                    skyboxMaterial.SetVector("_SunDirection", sky == "sunset"
                        ? new Vector4(0.15f, 0.12f, 0.95f, 0f)
                        : new Vector4(0.25f, 0.72f, 0.65f, 0f));
                }
                if (skyboxMaterial.HasProperty("_MoonDirection"))
                {
                    skyboxMaterial.SetVector("_MoonDirection", new Vector4(-0.35f, 0.55f, 0.65f, 0f));
                }
                if (skyboxMaterial.HasProperty("_MoonColor")) skyboxMaterial.SetColor("_MoonColor", new Color(0.75f, 0.88f, 1f));
                if (skyboxMaterial.HasProperty("_StarAmount")) skyboxMaterial.SetFloat("_StarAmount", sky == "night" ? 1f : 0f);
                if (skyboxMaterial.HasProperty("_HorizonGlow"))
                {
                    skyboxMaterial.SetColor("_HorizonGlow", sky == "sunset"
                        ? new Color(1f, 0.38f, 0.16f)
                        : sky == "night"
                            ? new Color(0.08f, 0.28f, 0.58f)
                            : new Color(0.62f, 0.86f, 0.82f));
                }
                if (skyboxMaterial.HasProperty("_HorizonGlowAmount"))
                {
                    skyboxMaterial.SetFloat("_HorizonGlowAmount", sky == "night" ? 0.32f : sky == "sunset" ? 0.48f : 0.18f);
                }
                if (skyboxMaterial.HasProperty("_PainterlyBands")) skyboxMaterial.SetFloat("_PainterlyBands", 0.8f);
            }
            if (sunLight != null)
            {
                sunLight.color = sunColor;
                sunLight.intensity = sunIntensity;
                sunLight.transform.rotation = Quaternion.Euler(sunPitch, -25f, 0f);
            }
            foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                camera.clearFlags = CameraClearFlags.Skybox;
            }
        }
    }
}
