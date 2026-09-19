namespace MarioKart.AI
{
    /// <summary>
    /// The single world-generation input: ONE image + ONE text description,
    /// shared by both players. There is intentionally no per-player variant
    /// of this type.
    /// </summary>
    public class WorldGenerationRequest
    {
        public byte[] imageBytes;
        public string imageFileName;
        public string description;

        public WorldGenerationRequest(byte[] imageBytes, string imageFileName, string description)
        {
            this.imageBytes = imageBytes;
            this.imageFileName = imageFileName;
            this.description = description;
        }
    }
}
