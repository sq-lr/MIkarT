using UnityEngine;

namespace MarioKart.UI
{
    /// <summary>
    /// The two comic-book fonts every piece of UI uses, loaded from
    /// Assets/Resources/Fonts (both SIL Open Font License, licences sit
    /// beside the .ttf files). Falls back to Unity's LegacyRuntime.ttf with
    /// one warning if a font asset is missing, so the UI never comes up
    /// blank.
    /// </summary>
    public static class GameFonts
    {
        private const string DisplayPath = "Fonts/Bangers-Regular";
        private const string BodyPath = "Fonts/ComicNeue-Bold";

        private static Font display, body;

        /// <summary>Bangers: loud all-caps comic lettering for titles, HUD, buttons.</summary>
        public static Font Display => Load(ref display, DisplayPath);

        /// <summary>Comic Neue Bold: a readable mixed-case comic hand for typed text and hints.</summary>
        public static Font Body => Load(ref body, BodyPath);

        private static Font Load(ref Font cache, string path)
        {
            if (cache != null) return cache;
            cache = Resources.Load<Font>(path);
            if (cache == null)
            {
                Debug.LogWarning($"GameFonts: '{path}' not found under Resources; falling back to LegacyRuntime.ttf");
                cache = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            return cache;
        }
    }
}
