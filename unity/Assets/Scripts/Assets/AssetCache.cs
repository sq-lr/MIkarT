using System.Collections.Generic;

namespace MarioKart.AssetsSystem
{
    /// <summary>
    /// Simple in-memory cache so AssetResolver doesn't rebuild an
    /// AssetDefinition every time the same object type is resolved.
    /// </summary>
    public class AssetCache
    {
        private readonly Dictionary<string, AssetDefinition> cache = new Dictionary<string, AssetDefinition>();

        public bool TryGet(string objectType, out AssetDefinition definition) => cache.TryGetValue(objectType, out definition);

        public void Store(string objectType, AssetDefinition definition) => cache[objectType] = definition;
    }
}
