using System.Collections.Generic;
using MarioKart.AI;
using UnityEngine;

namespace MarioKart.AssetsSystem
{
    /// <summary>
    /// Resolves a recipe object entry into an AssetDefinition: the primitive
    /// placeholder to spawn immediately, plus (if the backend is generating
    /// one) the mesh task whose GLB will replace it. Object types are
    /// whatever labels the vision model extracted from the photo, so unknown
    /// types are expected and get a neutral placeholder, not a warning.
    /// </summary>
    public interface IAssetResolver
    {
        AssetDefinition Resolve(WorldObjectEntry entry);
    }

    public class AssetResolver : IAssetResolver
    {
        private readonly AssetCache cache = new AssetCache();

        // Tuned placeholders for common labels. Anything else falls through
        // to the keyword heuristic below.
        private static readonly Dictionary<string, AssetDefinition> KnownTypes = new()
        {
            ["palm_tree"] = new AssetDefinition { objectType = "palm_tree", fallbackPrimitive = PrimitiveType.Cylinder, tintColor = new Color(0.42f, 0.58f, 0.36f), defaultScale = new Vector3(0.5f, 2f, 0.5f) },
            ["tree"] = new AssetDefinition { objectType = "tree", fallbackPrimitive = PrimitiveType.Cylinder, tintColor = new Color(0.38f, 0.52f, 0.32f), defaultScale = new Vector3(0.7f, 3.2f, 0.7f) },
            ["pine_tree"] = new AssetDefinition { objectType = "pine_tree", fallbackPrimitive = PrimitiveType.Cylinder, tintColor = new Color(0.32f, 0.46f, 0.34f), defaultScale = new Vector3(0.5f, 3.4f, 0.5f) },
            ["rock"] = new AssetDefinition { objectType = "rock", fallbackPrimitive = PrimitiveType.Sphere, tintColor = new Color(0.62f, 0.58f, 0.48f), defaultScale = new Vector3(1f, 0.7f, 1f) },
            ["cactus"] = new AssetDefinition { objectType = "cactus", fallbackPrimitive = PrimitiveType.Cylinder, tintColor = new Color(0.42f, 0.58f, 0.36f), defaultScale = new Vector3(0.4f, 1.5f, 0.4f) },
            ["bush"] = new AssetDefinition { objectType = "bush", fallbackPrimitive = PrimitiveType.Sphere, tintColor = new Color(0.46f, 0.62f, 0.36f), defaultScale = new Vector3(1.1f, 0.85f, 1.1f) },
        };

        public AssetDefinition Resolve(WorldObjectEntry entry)
        {
            string objectType = entry.type;
            string taskId = entry.asset?.task_id;

            if (cache.TryGet(objectType, out var cached) && cached.meshTaskId == taskId)
            {
                return cached;
            }

            var definition = KnownTypes.TryGetValue(objectType, out var known)
                ? Clone(known)
                : HeuristicPlaceholder(objectType);
            definition.meshTaskId = taskId;

            cache.Store(objectType, definition);
            return definition;
        }

        // Rough silhouette from the label so an unseen VLM label ("lantern",
        // "totem_pole", "boulder") still gets a plausible stand-in until its
        // generated mesh arrives.
        private static AssetDefinition HeuristicPlaceholder(string objectType)
        {
            string label = objectType.ToLowerInvariant();

            if (label.Contains("tree") || label.Contains("pole") || label.Contains("post") || label.Contains("lamp") || label.Contains("column"))
            {
                return new AssetDefinition { objectType = objectType, fallbackPrimitive = PrimitiveType.Cylinder, tintColor = new Color(0.55f, 0.48f, 0.38f), defaultScale = new Vector3(0.4f, 2f, 0.4f) };
            }
            if (label.Contains("rock") || label.Contains("boulder") || label.Contains("stone") || label.Contains("bush") || label.Contains("shrub"))
            {
                return new AssetDefinition { objectType = objectType, fallbackPrimitive = PrimitiveType.Sphere, tintColor = new Color(0.62f, 0.58f, 0.48f), defaultScale = new Vector3(1f, 0.7f, 1f) };
            }

            return new AssetDefinition { objectType = objectType, fallbackPrimitive = PrimitiveType.Cube, tintColor = new Color(0.72f, 0.68f, 0.58f), defaultScale = Vector3.one };
        }

        private static AssetDefinition Clone(AssetDefinition source)
        {
            return new AssetDefinition
            {
                objectType = source.objectType,
                prefab = source.prefab,
                fallbackPrimitive = source.fallbackPrimitive,
                tintColor = source.tintColor,
                defaultScale = source.defaultScale,
            };
        }
    }
}
