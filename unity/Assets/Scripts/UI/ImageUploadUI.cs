using System.Collections.Generic;
using MarioKart.AI;
using MarioKart.Core;
using MarioKart.UI.ImagePicker;
using UnityEngine;
using UnityEngine.UI;

namespace MarioKart.UI
{
    /// <summary>
    /// Shown on GameState.Input. Collects the single world-generation input
    /// (one image + one description) and hands it to GameManager. The image
    /// picker implementation is swapped via `picker` -- this panel never
    /// knows how the file was actually chosen. Awake restyles the baked
    /// controls as a comic page (photo panel + form panel on halftone
    /// paper) via ComicStyle; the pieces slam in each time the screen shows.
    /// </summary>
    public class ImageUploadUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Button chooseImageButton;
        [SerializeField] private RawImage previewImage;

        // The area inside the photo frame the preview may fill (canvas units).
        private static readonly Vector2 PreviewSlot = new Vector2(480f, 360f);
        [SerializeField] private InputField descriptionField;
        [SerializeField] private Toggle sunnySkyToggle;
        [SerializeField] private Toggle cloudySkyToggle;
        [SerializeField] private Toggle sunsetSkyToggle;
        [SerializeField] private Toggle nightSkyToggle;
        [SerializeField] private ToggleGroup skyToggleGroup;
        [SerializeField] private Transform skyOptionsContainer;
        [SerializeField] private Toggle personalizeToggle;
        [SerializeField] private Button generateButton;

        private IImagePicker picker;
        private byte[] pickedImageBytes;
        private string pickedImageFileName;
        private bool built;
        private Coroutine entrance;
        private readonly List<ComicStyle.Slam> slams = new List<ComicStyle.Slam>();

        private void Awake()
        {
#if UNITY_EDITOR
            picker = new EditorFileImagePicker();
#else
            picker = new PlaceholderImagePicker();
#endif
            EnsureSkyOptions();
            personalizeToggle = EnsurePersonalizeToggle(personalizeToggle);
            chooseImageButton.onClick.AddListener(OnChooseImageClicked);
            generateButton.onClick.AddListener(OnGenerateClicked);
            sunnySkyToggle.group = skyToggleGroup;
            cloudySkyToggle.group = skyToggleGroup;
            sunsetSkyToggle.group = skyToggleGroup;
            nightSkyToggle.group = skyToggleGroup;
            sunnySkyToggle.isOn = true;
            // Off by default: fast/free retrieval-only world unless the
            // player explicitly opts into slower, personalized generation.
            personalizeToggle.isOn = false;
            generateButton.interactable = false;
            foreach (var control in new Selectable[] { chooseImageButton, generateButton, sunnySkyToggle, cloudySkyToggle, sunsetSkyToggle, nightSkyToggle, personalizeToggle })
            {
                UISounds.Attach(control);
            }
            Decorate();
        }

        private void EnsureSkyOptions()
        {
            // The scene builder wires the toggles but not the container:
            // take the toggles' parent rather than spawning an empty one.
            if (skyOptionsContainer == null && sunnySkyToggle != null)
            {
                skyOptionsContainer = sunnySkyToggle.transform.parent;
            }
            if (skyOptionsContainer == null)
            {
                var optionsObject = new GameObject("Sky Options", typeof(RectTransform), typeof(HorizontalLayoutGroup));
                optionsObject.transform.SetParent((panel != null ? panel : gameObject).transform, false);
                skyOptionsContainer = optionsObject.transform;

                var optionsRect = optionsObject.GetComponent<RectTransform>();
                optionsRect.anchorMin = optionsRect.anchorMax = new Vector2(0.5f, 0.5f);
                optionsRect.pivot = new Vector2(0.5f, 0.5f);
                optionsRect.anchoredPosition = new Vector2(0f, -350f);
                optionsRect.sizeDelta = new Vector2(760f, 58f);
                var layout = optionsObject.GetComponent<HorizontalLayoutGroup>();
                layout.spacing = 12f;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;
            }

            if (skyToggleGroup == null)
            {
                skyToggleGroup = skyOptionsContainer.gameObject.GetComponent<ToggleGroup>();
                if (skyToggleGroup == null)
                {
                    skyToggleGroup = skyOptionsContainer.gameObject.AddComponent<ToggleGroup>();
                }
            }

            sunnySkyToggle = EnsureSkyToggle(sunnySkyToggle, "Sunny", "Sunny Sky");
            cloudySkyToggle = EnsureSkyToggle(cloudySkyToggle, "Cloudy", "Cloudy Sky");
            sunsetSkyToggle = EnsureSkyToggle(sunsetSkyToggle, "Sunset", "Sunset Sky");
            nightSkyToggle = EnsureSkyToggle(nightSkyToggle, "Night", "Night Sky");
        }

        private Toggle EnsureSkyToggle(Toggle toggle, string name, string label)
        {
            if (toggle != null) return toggle;

            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Toggle));
            buttonObject.transform.SetParent(skyOptionsContainer, false);
            buttonObject.GetComponent<RectTransform>().sizeDelta = new Vector2(130f, 42f);

            var background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.16f, 0.16f, 0.16f, 0.95f);

            var createdToggle = buttonObject.GetComponent<Toggle>();
            createdToggle.group = skyToggleGroup;
            createdToggle.targetGraphic = background; // Decorate() restyles it as a sky stamp

            var textObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(buttonObject.transform, false);
            var text = textObject.GetComponent<Text>();
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.font = GameFonts.Display; // same comic lettering as the scene-built buttons
            textObject.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            textObject.GetComponent<RectTransform>().anchorMax = Vector2.one;
            textObject.GetComponent<RectTransform>().offsetMin = Vector2.zero;
            textObject.GetComponent<RectTransform>().offsetMax = Vector2.zero;

            return createdToggle;
        }

        /// <summary>
        /// Self-constructs a standalone "Generate personalized assets" toggle
        /// if one wasn't wired in the Inspector, same fallback pattern as
        /// EnsureSkyOptions. Off by default -- see WorldGenerationRequest.personalize.
        /// </summary>
        private Toggle EnsurePersonalizeToggle(Toggle toggle)
        {
            if (toggle != null) return toggle;

            var buttonObject = new GameObject("PersonalizeToggle", typeof(RectTransform), typeof(Image), typeof(Toggle));
            buttonObject.transform.SetParent((panel != null ? panel : gameObject).transform, false);
            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -410f);
            rect.sizeDelta = new Vector2(340f, 42f);

            var background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.16f, 0.16f, 0.16f, 0.95f);

            var createdToggle = buttonObject.GetComponent<Toggle>();
            createdToggle.targetGraphic = background; // Decorate() restyles it as an inked checkbox

            var checkObject = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            checkObject.transform.SetParent(buttonObject.transform, false);
            var checkRect = checkObject.GetComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0f, 0.5f);
            checkRect.anchorMax = new Vector2(0f, 0.5f);
            checkRect.pivot = new Vector2(0f, 0.5f);
            checkRect.anchoredPosition = new Vector2(8f, 0f);
            checkRect.sizeDelta = new Vector2(26f, 26f);
            checkObject.GetComponent<Image>().color = new Color(0.55f, 0.72f, 0.20f);
            createdToggle.graphic = checkObject.GetComponent<Image>();

            var textObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(buttonObject.transform, false);
            var text = textObject.GetComponent<Text>();
            text.text = "Generate personalized assets";
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.font = GameFonts.Body;
            text.fontSize = 20;
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.offsetMin = new Vector2(42f, 0f);
            textRect.offsetMax = Vector2.zero;

            return createdToggle;
        }

        private void OnEnable()
        {
            GameManager.Instance.OnStateChanged += HandleStateChanged;
            HandleStateChanged(GameManager.Instance.CurrentState);
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStateChanged -= HandleStateChanged;
            }
        }

        private void HandleStateChanged(GameState state)
        {
            bool show = state == GameState.Input;
            if (show)
            {
                // Awake() only forces this off once, the very first time the
                // panel exists -- without also resetting it here, a toggle
                // switched on for one round (even by accident; it sits right
                // above the Generate button) would silently stay on for
                // every later round too, since nothing else ever clears it.
                personalizeToggle.isOn = false;
            }
            if (show && !panel.activeSelf)
            {
                panel.SetActive(true);
                RestartEntrance();
            }
            else if (!show)
            {
                StopEntrance();
                panel.SetActive(false);
            }
        }

        private void RestartEntrance()
        {
            StopEntrance();
            if (slams.Count > 0) entrance = StartCoroutine(ComicStyle.PlaySlams(slams));
        }

        private void StopEntrance()
        {
            if (entrance != null)
            {
                StopCoroutine(entrance);
                entrance = null;
            }
            ComicStyle.SnapToRest(slams);
        }

        /// <summary>
        /// Restyles the baked hierarchy in place as two comic panels -- the
        /// photo on the left, the form on the right -- without a scene
        /// rebuild. Controls are reached through the serialized fields so
        /// the Ensure* fallbacks get the same look; only the decorative
        /// texts are looked up by name and skipped (with a warning) if an
        /// older scene lacks them.
        /// </summary>
        private void Decorate()
        {
            if (built || panel == null) return;
            built = true;
            var root = panel.transform;

            ComicStyle.AddHalftoneBackdrop(panel, ComicStyle.Paper, new Color(ComicStyle.Blue.r, ComicStyle.Blue.g, ComicStyle.Blue.b, 0.20f), dotScale: 0.6f);

            var snap = ComicStyle.AddWordBurst(root, "SNAP!", new Vector2(-800f, 300f), new Vector2(280f, 200f), -12f, ComicStyle.Blue, fontSize: 48);
            var wow = ComicStyle.AddWordBurst(root, "WOW!", new Vector2(800f, 380f), new Vector2(240f, 190f), 10f, ComicStyle.Orange, fontSize: 48);

            var titleBox = ComicStyle.AddCaptionBox(root, "TitleBox", new Vector2(0f, 430f), new Vector2(1000f, 110f), ComicStyle.Yellow, -2f);
            var title = FindText("Title");
            if (title != null)
            {
                title.transform.SetParent(titleBox, false);
                ComicStyle.Stretch(title.rectTransform);
                ComicStyle.RestyleLabel(title, 64, ComicStyle.White);
                title.alignment = TextAnchor.MiddleCenter;
                title.text = "DESCRIBE YOUR WORLD!";
            }

            // Photo panel: the preview taped into a tilted frame.
            var photoFrame = ComicStyle.AddFrame(root, "PhotoFrame", new Vector2(-460f, 70f), new Vector2(560f, 440f), ComicStyle.White, -3f);
            var empty = ComicStyle.MakeLabel("Empty", photoFrame, "NO PHOTO YET", 30, new Color(ComicStyle.Ink.r, ComicStyle.Ink.g, ComicStyle.Ink.b, 0.45f), body: true);
            ComicStyle.Place(empty.rectTransform, Vector2.zero, PreviewSlot);
            if (previewImage != null)
            {
                previewImage.transform.SetParent(photoFrame, false);
                ComicStyle.Place(previewImage.rectTransform, Vector2.zero, PreviewSlot);
                previewImage.raycastTarget = false;
                previewImage.color = new Color(1f, 1f, 1f, 0f); // invisible until a photo is picked
            }
            foreach (var (x, angle) in new[] { (-200f, -35f), (200f, 35f) })
            {
                var tape = ComicStyle.MakeImage("Tape", photoFrame, ComicStyle.Frame(), new Color(ComicStyle.Yellow.r, ComicStyle.Yellow.g, ComicStyle.Yellow.b, 0.85f), Image.Type.Sliced);
                ComicStyle.Place(tape.rectTransform, new Vector2(x, 190f), new Vector2(130f, 28f), angle);
            }
            var photoTab = ComicStyle.AddCaptionBox(photoFrame, "PhotoTab", new Vector2(-210f, -190f), new Vector2(150f, 44f), ComicStyle.Yellow, -3f);
            var photoTabText = ComicStyle.MakeLabel("Label", photoTab, "PHOTO", 26, ComicStyle.White);
            ComicStyle.Stretch(photoTabText.rectTransform);

            var choose = chooseImageButton.GetComponent<RectTransform>();
            choose.SetParent(root, false);
            ComicStyle.Place(choose, new Vector2(-460f, -230f), new Vector2(360f, 80f), -3f);
            ComicStyle.StyleButton(chooseImageButton, ComicStyle.Blue, "CHOOSE IMAGE", 36);

            // Form panel: untilted so typing reads well.
            var formFrame = ComicStyle.AddFrame(root, "FormFrame", new Vector2(400f, 30f), new Vector2(900f, 580f), ComicStyle.White, 0f);

            var field = descriptionField.GetComponent<RectTransform>();
            field.SetParent(formFrame, false);
            ComicStyle.Place(field, new Vector2(0f, 200f), new Vector2(820f, 80f));
            ComicStyle.StyleInputField(descriptionField);

            var surfaceHint = FindText("SurfaceHint");
            if (surfaceHint != null)
            {
                surfaceHint.transform.SetParent(formFrame, false);
                ComicStyle.Place(surfaceHint.rectTransform, new Vector2(0f, 138f), new Vector2(820f, 30f));
                ComicStyle.RestyleLabel(surfaceHint, 22, new Color(ComicStyle.Ink.r, ComicStyle.Ink.g, ComicStyle.Ink.b, 0.7f), body: true);
                surfaceHint.alignment = TextAnchor.MiddleCenter;
            }

            var pickSky = ComicStyle.MakeLabel("PickSky", formFrame, "PICK A SKY:", 28, ComicStyle.White);
            ComicStyle.Place(pickSky.rectTransform, new Vector2(0f, 78f), new Vector2(820f, 40f));

            var skyGroup = skyOptionsContainer as RectTransform;
            if (skyGroup != null)
            {
                skyGroup.SetParent(formFrame, false);
                ComicStyle.Place(skyGroup, new Vector2(0f, -10f), new Vector2(820f, 110f));
                var layout = skyGroup.GetComponent<HorizontalLayoutGroup>(); // fallback-built groups only
                if (layout != null)
                {
                    layout.spacing = 20f;
                    layout.childAlignment = TextAnchor.MiddleCenter;
                }
            }
            var stamps = new[]
            {
                (sunnySkyToggle, "SUNNY", -300f, -3f, new Color(0.086f, 0.565f, 0.788f), new Color(0.722f, 0.925f, 0.91f)),
                (cloudySkyToggle, "CLOUDY", -100f, 2f, new Color(0.42f, 0.62f, 0.78f), new Color(0.72f, 0.80f, 0.78f)),
                (sunsetSkyToggle, "SUNSET", 100f, -2f, new Color(0.23f, 0.35f, 0.55f), new Color(1f, 0.85f, 0.54f)),
                (nightSkyToggle, "NIGHT", 300f, 3f, new Color(0.18f, 0.24f, 0.42f), new Color(0.35f, 0.32f, 0.40f)),
            };
            foreach (var (toggle, label, x, tilt, top, horizon) in stamps)
            {
                if (toggle == null) continue;
                var rect = toggle.GetComponent<RectTransform>();
                if (skyGroup != null && rect.parent != skyGroup) rect.SetParent(skyGroup, false);
                ComicStyle.Place(rect, new Vector2(x, 0f), new Vector2(180f, 104f));
                var element = toggle.GetComponent<LayoutElement>();
                if (skyGroup != null && skyGroup.GetComponent<HorizontalLayoutGroup>() != null)
                {
                    if (element == null) element = toggle.gameObject.AddComponent<LayoutElement>();
                    element.preferredWidth = 180f;
                    element.preferredHeight = 104f;
                }
                ComicStyle.StyleSkyStamp(toggle, top, horizon, label, tilt);
            }

            var personalize = personalizeToggle.GetComponent<RectTransform>();
            personalize.SetParent(formFrame, false);
            ComicStyle.Place(personalize, new Vector2(0f, -118f), new Vector2(560f, 50f));
            ComicStyle.StyleCheckbox(personalizeToggle);

            var generate = generateButton.GetComponent<RectTransform>();
            generate.SetParent(formFrame, false);
            ComicStyle.Place(generate, new Vector2(0f, -218f), new Vector2(440f, 100f), -1.5f);
            ComicStyle.StyleButton(generateButton, ComicStyle.Red, "GENERATE!", 46);

            // Entrance: title, the two panels, the word bursts, then the buttons land.
            slams.Add(new ComicStyle.Slam(titleBox, 0.00f, 0.28f, -2f, 1.35f, "Audio/kart_hit_0", 0.6f, 1.1f));
            slams.Add(new ComicStyle.Slam(photoFrame, 0.08f, 0.26f, -3f, 1.35f, "Audio/ui_click", 0.5f, 1.0f));
            slams.Add(new ComicStyle.Slam(formFrame, 0.14f, 0.26f, 0f, 1.35f));
            slams.Add(new ComicStyle.Slam(snap, 0.22f, 0.20f, -12f, 1.6f, "Audio/kart_hit_3", 0.3f, 1.5f));
            slams.Add(new ComicStyle.Slam(wow, 0.30f, 0.20f, 10f, 1.6f, "Audio/kart_hit_1", 0.3f, 1.3f));
            slams.Add(new ComicStyle.Slam(choose, 0.34f, 0.26f, -3f, 1.35f));
            slams.Add(new ComicStyle.Slam(generate, 0.42f, 0.28f, -1.5f, 1.35f, "Audio/kart_hit_2", 0.5f, 0.9f));

            // The baked panel is active on load; hide it so the first Input
            // state is a hidden->shown edge and plays the entrance.
            panel.SetActive(false);
        }

        private Text FindText(string name)
        {
            var child = panel.transform.Find(name);
            var text = child != null ? child.GetComponent<Text>() : null;
            if (text == null) Debug.LogWarning($"ImageUploadUI: baked child '{name}' not found -- scene older than the script? Rebuild via MarioKart > Build Main Scene.", this);
            return text;
        }

        /// <summary>
        /// Size the preview to the photo's aspect ratio inside the slot: as
        /// wide or as tall as the slot allows, never stretched, and centred
        /// along the other axis (the RawImage is anchored at the slot's
        /// centre).
        /// </summary>
        private void FitPreview(Texture texture)
        {
            if (texture == null || texture.width <= 0 || texture.height <= 0) return;
            float scale = Mathf.Min(PreviewSlot.x / texture.width, PreviewSlot.y / texture.height);
            previewImage.rectTransform.sizeDelta = new Vector2(texture.width * scale, texture.height * scale);
        }

        private void OnChooseImageClicked()
        {
            picker.PickImage(OnImagePicked, () => { });
        }

        private void OnImagePicked(byte[] bytes, string fileName)
        {
            pickedImageBytes = bytes;
            pickedImageFileName = fileName;

            var texture = new Texture2D(2, 2);
            if (texture.LoadImage(bytes))
            {
                // Re-picking used to leak the previous preview texture.
                if (previewImage.texture != null) Destroy(previewImage.texture);
                previewImage.texture = texture;
                previewImage.color = Color.white;
                FitPreview(texture);
            }
            else
            {
                Destroy(texture);
            }

            generateButton.interactable = true;
        }

        private void OnGenerateClicked()
        {
            if (pickedImageBytes == null) return;

            // The backend also defaults a blank description, but don't rely
            // on it: an empty form field used to come back as a 422 and a
            // silent fallback to the offline world.
            string description = string.IsNullOrWhiteSpace(descriptionField.text)
                ? "a racing world inspired by this photo"
                : descriptionField.text.Trim();
            // TEMPORARY diagnostic for the personalize=true-when-unchecked
            // report -- remove once resolved.
            Debug.Log($"[ImageUploadUI] personalizeToggle.isOn = {personalizeToggle.isOn} (instance {personalizeToggle.GetInstanceID()}) at Generate click");
            var request = new WorldGenerationRequest(
                pickedImageBytes,
                pickedImageFileName,
                description,
                SelectedSky(),
                personalizeToggle.isOn);
            GameManager.Instance.SubmitWorldInput(request);
        }

        private string SelectedSky()
        {
            if (cloudySkyToggle.isOn) return "cloudy";
            if (sunsetSkyToggle.isOn) return "sunset";
            if (nightSkyToggle.isOn) return "night";
            return "sunny";
        }
    }
}
