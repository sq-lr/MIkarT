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
            chooseImageButton.onClick.AddListener(OnChooseImageClicked);
            generateButton.onClick.AddListener(OnGenerateClicked);
            generateButton.interactable = false;
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

            var request = new WorldGenerationRequest(pickedImageBytes, pickedImageFileName, descriptionField.text);
            GameManager.Instance.SubmitWorldInput(request);
        }
    }
}
