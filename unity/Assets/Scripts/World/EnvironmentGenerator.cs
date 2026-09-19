using System.Collections.Generic;
using MarioKart.AI;
using MarioKart.AssetsSystem;
using MarioKart.Core;
using UnityEngine;

namespace MarioKart.World
{
    /// <summary>
    /// Places WorldRecipe.objects[] around the generated track. Deterministic
    /// via a per-object-type seed derived from the environment seed, so
    /// adding/removing one object type doesn't reshuffle another's placement.
    /// Spawns primitive placeholders immediately; GeneratedMeshLoader swaps
    /// backend-generated meshes in over them later without moving them.
    /// Placeholder density model only -- no spatial system yet.
    /// </summary>
    public class EnvironmentGenerator : MonoBehaviour
    {
        // Optional: on the same GameObject (or assigned in the Inspector).
        // Without it, placeholders are simply never replaced.
        [SerializeField] private GeneratedMeshLoader meshLoader;

        // Interfaces aren't Unity-serializable, so this is wired in code
        // (not the Inspector).
        private readonly IAssetResolver resolver = new AssetResolver();

        // Placeholder linear model: density (0-1) * scale * control point
        // count = roughly how many instances of that object type to place.
        private const float DensityToCountScale = 1.5f;
        private const float MinOutwardOffset = 1f;
        private const float MaxOutwardOffset = 6f;

        private void Awake()
        {
            if (meshLoader == null)
            {
                meshLoader = GetComponent<GeneratedMeshLoader>();
            }
        }

        public void Generate(WorldRecipe recipe, GeneratedTrack track, int seed)
        {
            if (meshLoader != null)
            {
                meshLoader.Clear();
            }

            foreach (Transform child in transform)
            {
                Destroy(child.gameObject);
            }

            var definitions = new List<AssetDefinition>();
            var placeholdersByType = new Dictionary<string, List<GameObject>>();

            foreach (var entry in recipe.objects)
            {
                int objectSeed = WorldRandom.DeriveSeed(seed, entry.type);
                var rng = new WorldRandom(objectSeed);

                int count = Mathf.RoundToInt(entry.density * DensityToCountScale * track.controlPoints.Count);
                var definition = resolver.Resolve(entry);
                definitions.Add(definition);

                var instances = new List<GameObject>(count);
                placeholdersByType[entry.type] = instances;

                for (int i = 0; i < count; i++)
                {
                    var basePoint = track.controlPoints[rng.NextInt(0, track.controlPoints.Count)];
                    var outward = basePoint.normalized;
                    float offset = track.width * 0.5f + rng.NextRange(MinOutwardOffset, MaxOutwardOffset);
                    var position = basePoint + outward * offset;

                    instances.Add(SpawnPlaceholder(definition, position, rng.NextRange(0f, 360f)));
                }
            }

            var config = GameManager.Instance != null ? GameManager.Instance.Config : null;
            if (meshLoader != null && (config == null || config.enableGeneratedMeshes))
            {
                meshLoader.Begin(definitions, placeholdersByType);
            }
        }

        private GameObject SpawnPlaceholder(AssetDefinition definition, Vector3 position, float yRotation)
        {
            GameObject instance = definition.prefab != null
                ? Instantiate(definition.prefab)
                : GameObject.CreatePrimitive(definition.fallbackPrimitive);

            instance.name = $"{definition.objectType}_Placeholder";
            instance.transform.SetParent(transform, worldPositionStays: false);
            instance.transform.position = position;
            instance.transform.rotation = Quaternion.Euler(0f, yRotation, 0f);
            instance.transform.localScale = definition.defaultScale;

            if (definition.prefab == null)
            {
                var renderer = instance.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = definition.tintColor;
                }
            }

            return instance;
        }
    }
}
