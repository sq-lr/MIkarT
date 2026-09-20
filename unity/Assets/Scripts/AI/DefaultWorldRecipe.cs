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
        public static WorldRecipe Get(string sky = "sunny")
        {
            var recipe = new WorldRecipe
            {
                version = 1,
                seed = 1,
                world = new WorldInfo
                {
                    name = "Kusakabe Countryside",
                    theme = "countryside",
                    terrain = "grass",
                    weather = "haze",
                    time_of_day = "day",
                    sky = "sunny",
                    mood = "energetic",
                },
                track = new TrackInfo { width = 16f, length = 600f, difficulty = 0.4f, surface = "concrete" },
                objects = new List<WorldObjectEntry>
                {
                    new WorldObjectEntry { type = "tree", density = 0.45f, placement = "scattered" },
                    new WorldObjectEntry { type = "bush", density = 0.22f, placement = "scattered" },
                    new WorldObjectEntry { type = "rock", density = 0.12f, placement = "scattered" },
                },
                palette = new List<string> { "#F4E6C0", "#6B8F4A", "#A8C8DC" },
            };
            recipe.world.sky = sky;
            return recipe;
        }
    }
}
