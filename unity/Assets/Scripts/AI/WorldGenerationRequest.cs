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
        public string sky;

        // Off by default: skips Meshy generation entirely and sources every
        // object (including up to 2 landmarks) from library retrieval
        // instead -- fast and free, at the cost of not reflecting the
        // specific things in the photo/description. See
        // docs/decisions/0010-personalize-toggle.md.
        public bool personalize;

        public WorldGenerationRequest(byte[] imageBytes, string imageFileName, string description, string sky, bool personalize)
        {
            this.imageBytes = imageBytes;
            this.imageFileName = imageFileName;
            this.description = description;
            this.sky = sky;
            this.personalize = personalize;
        }
    }
}
