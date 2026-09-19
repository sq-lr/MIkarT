using System;
using UnityEngine;

namespace MarioKart.UI.ImagePicker
{
    /// <summary>
    /// Runtime-safe fallback for standalone builds, where no native file
    /// dialog is available. Loads a bundled sample image from Resources/
    /// instead of prompting the user. Swap for a real native-plugin-backed
    /// picker later without touching ImageUploadUI.
    /// </summary>
    public class PlaceholderImagePicker : IImagePicker
    {
        private const string ResourceName = "sample_world_image";

        public void PickImage(Action<byte[], string> onPicked, Action onCancelled)
        {
            var texture = Resources.Load<Texture2D>(ResourceName);
            if (texture == null)
            {
                Debug.LogWarning($"PlaceholderImagePicker: missing Resources/{ResourceName}");
                onCancelled?.Invoke();
                return;
            }

            byte[] bytes = texture.EncodeToPNG();
            onPicked?.Invoke(bytes, $"{ResourceName}.png");
        }
    }
}
