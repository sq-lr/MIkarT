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
            ["palm_tree"] = new AssetDefinition { objectType = "palm_tree", fallbackPrimitive = PrimitiveType.Cylinder, tintColor = new Color(0.18f, 0.55f, 0.34f), defaultScale = new Vector3(0.5f, 2f, 0.5f) },
            ["tree"] = new AssetDefinition { objectType = "tree", fallbackPrimitive = PrimitiveType.Cylinder, tintColor = new Color(0.30f, 0.42f, 0.24f), defaultScale = new Vector3(0.6f, 2.5f, 0.6f) },
            ["pine_tree"] = new AssetDefinition { objectType = "pine_tree", fallbackPrimitive = PrimitiveType.Cylinder, tintColor = new Color(0.20f, 0.35f, 0.25f), defaultScale = new Vector3(0.5f, 3f, 0.5f) },
            ["rock"] = new AssetDefinition { objectType = "rock", fallbackPrimitive = PrimitiveType.Sphere, tintColor = Color.gray, defaultScale = new Vector3(1f, 0.7f, 1f) },
            ["cactus"] = new AssetDefinition { objectType = "cactus", fallbackPrimitive = PrimitiveType.Cylinder, tintColor = new Color(0.25f, 0.5f, 0.25f), defaultScale = new Vector3(0.4f, 1.5f, 0.4f) },
            ["bush"] = new AssetDefinition { objectType = "bush", fallbackPrimitive = PrimitiveType.Sphere, tintColor = new Color(0.25f, 0.45f, 0.2f), defaultScale = new Vector3(0.8f, 0.6f, 0.8f) },
            ["banana"] = new AssetDefinition { objectType = "banana", fallbackPrimitive = PrimitiveType.Sphere, tintColor = new Color(0.95f, 0.82f, 0.15f), defaultScale = new Vector3(0.9f, 0.45f, 0.45f) },
            ["mushroom"] = new AssetDefinition { objectType = "mushroom", fallbackPrimitive = PrimitiveType.Capsule, tintColor = new Color(0.9f, 0.2f, 0.2f), defaultScale = new Vector3(0.55f, 0.7f, 0.55f) },
            ["shell"] = new AssetDefinition { objectType = "shell", fallbackPrimitive = PrimitiveType.Sphere, tintColor = new Color(0.25f, 0.75f, 0.3f), defaultScale = Vector3.one * 0.7f },
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
                return new AssetDefinition { objectType = objectType, fallbackPrimitive = PrimitiveType.Cylinder, tintColor = new Color(0.45f, 0.4f, 0.35f), defaultScale = new Vector3(0.4f, 2f, 0.4f) };
            }
            if (label.Contains("rock") || label.Contains("boulder") || label.Contains("stone") || label.Contains("bush") || label.Contains("shrub"))
            {
                return new AssetDefinition { objectType = objectType, fallbackPrimitive = PrimitiveType.Sphere, tintColor = Color.gray, defaultScale = new Vector3(1f, 0.7f, 1f) };
            }

            return new AssetDefinition { objectType = objectType, fallbackPrimitive = PrimitiveType.Cube, tintColor = new Color(0.6f, 0.6f, 0.6f), defaultScale = Vector3.one };
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
