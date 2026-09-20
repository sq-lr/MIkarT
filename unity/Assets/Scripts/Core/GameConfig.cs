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
        // /generate-world now runs 3 concurrent Claude calls (vision, text,
        // filler) plus sequential Meshy/Poly Pizza submissions for whatever
        // they find -- realistically 15-20s+ with all real providers on, so
        // 15s was cutting it close enough to trip the offline fallback on a
        // slow request rather than a real failure.
        public float requestTimeoutSeconds = 60f;
        public int defaultLapCount = 3;
        public float countdownSeconds = 3f;
        public bool fallbackToDefaultOnError = true;

        // Generated-mesh delivery (see docs/decisions/0006). Meshes arrive
        // minutes after the race starts; placeholders are used until then.
        public bool enableGeneratedMeshes = true;
        public float assetPollIntervalSeconds = 5f;
        public float assetPollTimeoutSeconds = 600f;

        // Hold the "Generating" screen until every generated mesh has landed
        // (or failed / timed out), instead of racing on placeholders while
        // they stream in. On timeout the race starts anyway and any mesh
        // that finishes later still swaps in -- the game never gets stuck.
        // Ignored when the player's "Generate personalized assets" toggle
        // was on: that request always waits, with no timeout, regardless of
        // these two values -- see GameManager.BuildWorldAndAdvance.
        public bool waitForGeneratedMeshes = true;
        public float meshWaitTimeoutSeconds = 300f;

        // Cel-shaded look (see docs/decisions/0008). Off = plain Standard
        // shader on everything, including swapped-in generated meshes.
        public bool toonShading = true;
        // Comic-print halftone dots in the shadow bands: 0 = flat bands only,
        // 1 = pure ink-on-paper dots. Cell size is in screen pixels.
        public float toonDotStrength = 0.7f;
        public float toonDotSizePixels = 6f;
        // Faint static paper grain over each camera's frame (PaperGrainEffect).
        // Strength is the +/- brightness swing (0.04 = 4%); 0 turns it off.
        // Scale is screen pixels per grain texel: bigger = coarser paper.
        public float paperGrainStrength = 0.04f;
        public float paperGrainScalePixels = 1.5f;
        // How far from the camera cast shadows are drawn. Smaller = sharper
        // shadows near the karts; nothing beyond it casts one.
        public float shadowDistance = 70f;
    }
}
