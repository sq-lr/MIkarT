using System;

namespace MarioKart.Core
{
    /// <summary>
    /// Tunable game settings. Plain serializable class rather than a
    /// ScriptableObject so it needs no hand-authored .asset file -- it is
    /// embedded directly as a [SerializeField] on GameManager and tuned in
    /// the Inspector there.
    /// </summary>
    [Serializable]
    public class GameConfig
    {
        public string backendBaseUrl = "http://localhost:8000";
        public float requestTimeoutSeconds = 15f;
        public int defaultLapCount = 3;
        public float countdownSeconds = 3f;
        public bool fallbackToDefaultOnError = true;

        // Generated-mesh delivery (see docs/decisions/0006). Meshes arrive
        // minutes after the race starts; placeholders are used until then.
        public bool enableGeneratedMeshes = true;
        public float assetPollIntervalSeconds = 5f;
        public float assetPollTimeoutSeconds = 600f;
    }
}
