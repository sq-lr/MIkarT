using System;

namespace MarioKart.UI.ImagePicker
{
    /// <summary>
    /// Abstraction over "get image bytes from the local computer" so the
    /// rest of the game never depends on how the file was chosen. Webcam
    /// capture and drag-and-drop are explicitly out of scope for bootstrap.
    /// </summary>
    public interface IImagePicker
    {
        void PickImage(Action<byte[], string> onPicked, Action onCancelled);
    }
}
