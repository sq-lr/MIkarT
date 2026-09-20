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
    /// knows how the file was actually chosen.
    /// </summary>
    public class ImageUploadUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Button chooseImageButton;
        [SerializeField] private RawImage previewImage;
        [SerializeField] private InputField descriptionField;
        [SerializeField] private Toggle sunnySkyToggle;
        [SerializeField] private Toggle cloudySkyToggle;
        [SerializeField] private Toggle sunsetSkyToggle;
        [SerializeField] private Toggle nightSkyToggle;
        [SerializeField] private ToggleGroup skyToggleGroup;
        [SerializeField] private Transform skyOptionsContainer;
        [SerializeField] private Button generateButton;

        private IImagePicker picker;
        private byte[] pickedImageBytes;
        private string pickedImageFileName;

        private void Awake()
        {
#if UNITY_EDITOR
            picker = new EditorFileImagePicker();
#else
            picker = new PlaceholderImagePicker();
#endif
            EnsureSkyOptions();
            chooseImageButton.onClick.AddListener(OnChooseImageClicked);
            generateButton.onClick.AddListener(OnGenerateClicked);
            sunnySkyToggle.group = skyToggleGroup;
            cloudySkyToggle.group = skyToggleGroup;
            sunsetSkyToggle.group = skyToggleGroup;
            nightSkyToggle.group = skyToggleGroup;
            sunnySkyToggle.isOn = true;
            generateButton.interactable = false;
        }

        private void EnsureSkyOptions()
        {
            if (skyOptionsContainer == null)
            {
                var optionsObject = new GameObject("Sky Options", typeof(RectTransform), typeof(HorizontalLayoutGroup));
                optionsObject.transform.SetParent((panel != null ? panel : gameObject).transform, false);
                skyOptionsContainer = optionsObject.transform;

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
            createdToggle.targetGraphic = background;
            createdToggle.colors = new ColorBlock
            {
                normalColor = new Color(0.16f, 0.16f, 0.16f),
                highlightedColor = new Color(0.30f, 0.50f, 0.80f),
                pressedColor = new Color(0.20f, 0.40f, 0.70f),
                selectedColor = new Color(0.25f, 0.65f, 0.35f),
                disabledColor = new Color(0.10f, 0.10f, 0.10f),
                colorMultiplier = 1f,
                fadeDuration = 0.1f,
            };

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
            panel.SetActive(state == GameState.Input);
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
                previewImage.texture = texture;
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
            var request = new WorldGenerationRequest(
                pickedImageBytes,
                pickedImageFileName,
                description,
                SelectedSky());
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
