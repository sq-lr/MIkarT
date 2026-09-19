using System.Collections.Generic;

namespace MarioKart.AI
{
    /// <summary>
    /// Hardcoded fallback used whenever the backend call fails or times out,
    /// so the game stays fully playable offline. Not part of the original
    /// file listing in the spec -- added because the fallback requirement
    /// needs somewhere to live.
    /// </summary>
    public static class DefaultWorldRecipe
    {
        public static WorldRecipe Get()
        {
            return new WorldRecipe
            {
                version = 1,
                seed = 1,
                world = new WorldInfo
                {
                    name = "Generic Racing World",
                    theme = "generic",
                    terrain = "grass",
                    weather = "clear",
                    time_of_day = "day",
                },
                track = new TrackInfo { width = 16f, length = 600f, difficulty = 0.4f },
                objects = new List<WorldObjectEntry>
                {
                    new WorldObjectEntry { type = "tree", density = 0.3f },
                    new WorldObjectEntry { type = "rock", density = 0.15f },
                },
                palette = new List<string> { "#4C6B3A", "#8C8C8C", "#6EC6FF" },
            };
        }
    }
}
