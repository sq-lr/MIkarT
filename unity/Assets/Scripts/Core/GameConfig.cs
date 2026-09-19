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
    }
}
