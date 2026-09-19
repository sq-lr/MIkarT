using System;

namespace MarioKart.World
{
    /// <summary>
    /// The only source of randomness procedural generation should use.
    /// Wraps System.Random (never UnityEngine.Random's global state) so
    /// results are reproducible independent of frame timing or call order
    /// elsewhere in the game. Not in the original file listing, but required
    /// by the spec's determinism guarantee (same recipe + seed = same world).
    /// </summary>
    public class WorldRandom
    {
        private readonly Random rng;

        public WorldRandom(int seed)
        {
            rng = new Random(seed);
        }

        public float NextFloat() => (float)rng.NextDouble();

        public float NextRange(float min, float max) => min + NextFloat() * (max - min);

        public int NextInt(int minInclusive, int maxExclusive) => rng.Next(minInclusive, maxExclusive);

        /// <summary>
        /// Derives a stable sub-seed for one procedural system/object type
        /// from a base seed and a label, so e.g. adding a new object type to
        /// the recipe never perturbs the placement of existing types.
        /// </summary>
        public static int DeriveSeed(int baseSeed, string systemLabel)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + baseSeed;
                foreach (char c in systemLabel)
                {
                    hash = hash * 31 + c;
                }
                return hash;
            }
        }
    }
}
