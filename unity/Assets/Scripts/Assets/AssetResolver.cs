using System.Collections.Generic;
using UnityEngine;

namespace MarioKart.AssetsSystem
{
    /// <summary>
    /// Resolves a recipe object type ("palm_tree", "rock", ...) into an
    /// AssetDefinition. Not coupled to any external asset provider -- today
    /// it returns local primitive placeholders. A future
    /// ExternalAssetResolver can implement this same interface to fetch real
    /// 3D assets without EnvironmentGenerator changing at all.
    /// </summary>
    public interface IAssetResolver
    {
        AssetDefinition Resolve(string objectType);
    }

    public class AssetResolver : IAssetResolver
    {
        private readonly AssetCache cache = new AssetCache();

        // Hardcoded placeholder registry. Extend as new object types appear
        // in the WorldRecipe schema's vocabulary.
        private static readonly Dictionary<string, AssetDefinition> KnownTypes = new()
        {
            ["palm_tree"] = new AssetDefinition { objectType = "palm_tree", fallbackPrimitive = PrimitiveType.Cylinder, tintColor = new Color(0.18f, 0.55f, 0.34f), defaultScale = new Vector3(0.5f, 2f, 0.5f) },
            ["tree"] = new AssetDefinition { objectType = "tree", fallbackPrimitive = PrimitiveType.Cylinder, tintColor = new Color(0.30f, 0.42f, 0.24f), defaultScale = new Vector3(0.6f, 2.5f, 0.6f) },
            ["pine_tree"] = new AssetDefinition { objectType = "pine_tree", fallbackPrimitive = PrimitiveType.Cylinder, tintColor = new Color(0.20f, 0.35f, 0.25f), defaultScale = new Vector3(0.5f, 3f, 0.5f) },
            ["rock"] = new AssetDefinition { objectType = "rock", fallbackPrimitive = PrimitiveType.Sphere, tintColor = Color.gray, defaultScale = new Vector3(1f, 0.7f, 1f) },
            ["cactus"] = new AssetDefinition { objectType = "cactus", fallbackPrimitive = PrimitiveType.Cylinder, tintColor = new Color(0.25f, 0.5f, 0.25f), defaultScale = new Vector3(0.4f, 1.5f, 0.4f) },
            ["bush"] = new AssetDefinition { objectType = "bush", fallbackPrimitive = PrimitiveType.Sphere, tintColor = new Color(0.25f, 0.45f, 0.2f), defaultScale = new Vector3(0.8f, 0.6f, 0.8f) },
        };

        public AssetDefinition Resolve(string objectType)
        {
            if (cache.TryGet(objectType, out var cached))
            {
                return cached;
            }

            if (!KnownTypes.TryGetValue(objectType, out var definition))
            {
                Debug.LogWarning($"AssetResolver: unknown object type '{objectType}', using generic placeholder");
                definition = new AssetDefinition { objectType = objectType, fallbackPrimitive = PrimitiveType.Cube, tintColor = Color.gray };
            }

            cache.Store(objectType, definition);
            return definition;
        }
    }
}
