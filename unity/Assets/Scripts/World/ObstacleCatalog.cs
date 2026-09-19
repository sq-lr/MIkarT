using UnityEngine;

namespace MarioKart.World
{
    public enum ObstacleKind
    {
        Boost,
        Paralyze,
        Spin,
    }

    /// <summary>
    /// Maps a WorldRecipe object label onto an obstacle power when that
    /// asset is used as a track pickup. Unknown labels are still usable as
    /// skins (they just don't prefer a specific power).
    /// </summary>
    public static class ObstacleCatalog
    {
        public static bool TryKindForLabel(string objectType, out ObstacleKind kind)
        {
            kind = default;
            if (string.IsNullOrEmpty(objectType)) return false;

            string label = objectType.ToLowerInvariant().Replace(' ', '_').Replace('-', '_');

            if (Matches(label, "banana", "peel"))
            {
                kind = ObstacleKind.Spin;
                return true;
            }
            if (Matches(label, "mushroom", "star", "boost", "turbo", "feather", "pepper"))
            {
                kind = ObstacleKind.Boost;
                return true;
            }
            if (Matches(label, "shell", "ice", "freeze", "bomb", "oil", "spike", "stun", "trap", "snow"))
            {
                kind = ObstacleKind.Paralyze;
                return true;
            }

            return false;
        }

        private static bool Matches(string label, params string[] tokens)
        {
            foreach (var token in tokens)
            {
                if (label == token || label.Contains(token)) return true;
            }
            return false;
        }
    }
}
