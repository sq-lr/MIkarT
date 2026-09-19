#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;

namespace MarioKart.UI.ImagePicker
{
    /// <summary>
    /// Real native file-open dialog, only available when running inside the
    /// Unity Editor (no cross-platform runtime file dialog ships with Unity
    /// -- that requires a third-party native plugin, out of scope here).
    /// Use PlaceholderImagePicker for standalone builds.
    /// </summary>
    public class EditorFileImagePicker : IImagePicker
    {
        public void PickImage(Action<byte[], string> onPicked, Action onCancelled)
        {
            string path = EditorUtility.OpenFilePanel("Select Image", "", "png,jpg,jpeg");
            if (string.IsNullOrEmpty(path))
            {
                onCancelled?.Invoke();
                return;
            }

            byte[] bytes = File.ReadAllBytes(path);
            onPicked?.Invoke(bytes, Path.GetFileName(path));
        }
    }
}
#endif
