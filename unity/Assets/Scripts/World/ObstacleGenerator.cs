using System.Collections;
using System.Collections.Generic;
using MarioKart.AI;
using MarioKart.AssetsSystem;
using UnityEngine;

namespace MarioKart.World
{
    /// <summary>
    /// Drops pass-through pickups on the racing line. Placement is random
    /// every Generate() call (a time salt is mixed into the derived seed) so
    /// a new game — or Play Again — reshuffles them. Collecting one destroys
    /// it and spawns a replacement elsewhere. Track/environment generation
    /// stays deterministic from the recipe seed.
    ///
    /// Visuals prefer WorldRecipe assets: a label mapped in ObstacleCatalog
    /// (e.g. banana → spin) skins that power; any other photo object can
    /// still replace the default ball. GeneratedMeshLoader swaps the GLB
    /// onto the pickup without changing its power.
    /// </summary>
    public class ObstacleGenerator : MonoBehaviour
    {
        [SerializeField] private GeneratedMeshLoader meshLoader;

        private const int Count = 12;
        private const float StartSkip = 0.1f;
        private const float LateralFraction = 0.35f;
        private const float Height = 0.7f;
        private const float PickupScale = 0.9f;
        private const float TriggerRadius = 0.55f;
        private const float RespawnDelay = 0.45f;
        private const float MinRespawnDistance = 14f;

        private readonly IAssetResolver resolver = new AssetResolver();
        private readonly Dictionary<ObstacleKind, List<AssetDefinition>> kindSkins =
            new Dictionary<ObstacleKind, List<AssetDefinition>>();
        private readonly List<AssetDefinition> genericSkins = new List<AssetDefinition>();

        private GeneratedTrack track;
        private WorldRandom rng;

        public void Configure(GeneratedMeshLoader loader)
        {
            if (loader != null) meshLoader = loader;
        }

        public void Generate(GeneratedTrack generatedTrack, WorldRecipe recipe, int recipeSeed)
        {
            StopAllCoroutines();
            ClearChildren();

            track = generatedTrack;
            if (track == null || track.controlPoints == null || track.controlPoints.Count < 2) return;

            int salt = unchecked(System.Environment.TickCount);
            int seed = WorldRandom.DeriveSeed(WorldRandom.DeriveSeed(recipeSeed, "obstacles"), salt.ToString());
            rng = new WorldRandom(seed);

            BuildSkins(recipe);

            var kinds = MixKinds();
            var placeholdersByType = new Dictionary<string, List<GameObject>>();
            var definitions = new List<AssetDefinition>();

            for (int i = 0; i < Count; i++)
            {
                var obstacle = Spawn(kinds[i], RandomPose(), out var skin);
                if (skin != null) RegisterSkin(skin, obstacle, definitions, placeholdersByType);
            }

            if (meshLoader != null && definitions.Count > 0)
            {
                meshLoader.Begin(definitions, placeholdersByType);
            }
        }

        public void OnCollected(TrackObstacle obstacle)
        {
            if (obstacle == null) return;
            Destroy(obstacle.gameObject);
            if (isActiveAndEnabled) StartCoroutine(RespawnSoon());
        }

        private IEnumerator RespawnSoon()
        {
            yield return new WaitForSeconds(RespawnDelay);
            if (track == null || rng == null) yield break;

            var kind = (ObstacleKind)rng.NextInt(0, 3);
            var obstacle = Spawn(kind, RandomPose(MinRespawnDistance), out var skin);
            if (skin != null && skin.HasGeneratedMesh && meshLoader != null)
            {
                meshLoader.AttachPlaceholder(skin.objectType, obstacle);
            }
        }

        private void BuildSkins(WorldRecipe recipe)
        {
            kindSkins.Clear();
            genericSkins.Clear();
            kindSkins[ObstacleKind.Boost] = new List<AssetDefinition>();
            kindSkins[ObstacleKind.Paralyze] = new List<AssetDefinition>();
            kindSkins[ObstacleKind.Spin] = new List<AssetDefinition>();

            if (recipe?.objects == null) return;

            foreach (var entry in recipe.objects)
            {
                if (entry == null || string.IsNullOrEmpty(entry.type)) continue;
                var definition = resolver.Resolve(entry);
                genericSkins.Add(definition);
                if (ObstacleCatalog.TryKindForLabel(entry.type, out var kind))
                {
                    kindSkins[kind].Add(definition);
                }
            }
        }

        private AssetDefinition PickSkin(ObstacleKind kind)
        {
            if (kindSkins.TryGetValue(kind, out var preferred) && preferred.Count > 0)
            {
                return preferred[rng.NextInt(0, preferred.Count)];
            }
            if (genericSkins.Count > 0)
            {
                return genericSkins[rng.NextInt(0, genericSkins.Count)];
            }
            return null;
        }

        private List<ObstacleKind> MixKinds()
        {
            var kinds = new List<ObstacleKind>(Count);
            kinds.Add(ObstacleKind.Boost);
            kinds.Add(ObstacleKind.Boost);
            kinds.Add(ObstacleKind.Paralyze);
            kinds.Add(ObstacleKind.Paralyze);
            kinds.Add(ObstacleKind.Spin);
            kinds.Add(ObstacleKind.Spin);
            for (int i = kinds.Count; i < Count; i++)
            {
                kinds.Add((ObstacleKind)(i % 3));
            }

            for (int i = kinds.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(0, i + 1);
                var tmp = kinds[i];
                kinds[i] = kinds[j];
                kinds[j] = tmp;
            }
            return kinds;
        }

        private GameObject Spawn(ObstacleKind kind, Pose pose, out AssetDefinition skin)
        {
            skin = PickSkin(kind);

            var go = new GameObject(skin != null ? $"Obstacle_{kind}_{skin.objectType}" : $"Obstacle_{kind}");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);

            var trigger = go.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = TriggerRadius;

            BuildVisual(go.transform, kind, skin);

            var obstacle = go.AddComponent<TrackObstacle>();
            obstacle.Configure(kind, rng.NextFloat() * Mathf.PI * 2f, this);
            return go;
        }

        private static void BuildVisual(Transform root, ObstacleKind kind, AssetDefinition skin)
        {
            PrimitiveType primitive = skin != null ? skin.fallbackPrimitive : PrimitiveType.Sphere;
            var visual = GameObject.CreatePrimitive(primitive);
            visual.name = "Visual";
            visual.transform.SetParent(root, worldPositionStays: false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = PickupScaleOf(skin);

            var visualCollider = visual.GetComponent<Collider>();
            if (visualCollider != null) Destroy(visualCollider);

            var renderer = visual.GetComponent<Renderer>();
            if (renderer != null)
            {
                Color tint = skin != null ? Color.Lerp(skin.tintColor, TrackObstacle.KindColor(kind), 0.35f)
                                          : TrackObstacle.KindColor(kind);
                renderer.material.color = tint;
            }
        }

        private static Vector3 PickupScaleOf(AssetDefinition skin)
        {
            if (skin == null) return Vector3.one * PickupScale;
            Vector3 s = skin.defaultScale;
            float max = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            if (max < 0.0001f) return Vector3.one * PickupScale;
            return s / max * PickupScale;
        }

        private static void RegisterSkin(
            AssetDefinition skin,
            GameObject obstacle,
            List<AssetDefinition> definitions,
            Dictionary<string, List<GameObject>> placeholdersByType)
        {
            if (!placeholdersByType.TryGetValue(skin.objectType, out var list))
            {
                list = new List<GameObject>();
                placeholdersByType[skin.objectType] = list;
                definitions.Add(skin);
            }
            list.Add(obstacle);
        }

        private Pose RandomPose(float minDistanceFromExisting = 0f)
        {
            Pose best = SamplePose(StartSkip + rng.NextFloat() * (1f - StartSkip));
            if (minDistanceFromExisting <= 0f) return best;

            float bestScore = NearestExisting(best.position);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var candidate = SamplePose(StartSkip + rng.NextFloat() * (1f - StartSkip));
                float score = NearestExisting(candidate.position);
                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
                if (bestScore >= minDistanceFromExisting) break;
            }
            return best;
        }

        private Pose SamplePose(float t)
        {
            t += rng.NextRange(-0.015f, 0.015f);
            SampleTrack(track, t, out Vector3 center, out Vector3 tangent);
            Vector3 across = Vector3.Cross(Vector3.up, tangent);
            if (across.sqrMagnitude < 0.0001f) across = Vector3.right;
            across.Normalize();

            float lateral = rng.NextRange(-LateralFraction, LateralFraction) * (track.width * 0.5f);
            Vector3 position = center + across * lateral;
            position.y = Height;
            return new Pose(position, Quaternion.LookRotation(tangent, Vector3.up));
        }

        private float NearestExisting(Vector3 position)
        {
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child == null) continue;
                float d = Vector3.Distance(position, child.position);
                if (d < nearest) nearest = d;
            }
            return nearest;
        }

        private void ClearChildren()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }
        }

        private static void SampleTrack(GeneratedTrack track, float t, out Vector3 position, out Vector3 tangent)
        {
            var points = track.controlPoints;
            int n = points.Count;
            float scaled = Mathf.Repeat(t, 1f) * n;
            int i0 = Mathf.FloorToInt(scaled) % n;
            int i1 = (i0 + 1) % n;
            float local = scaled - Mathf.Floor(scaled);

            position = Vector3.Lerp(points[i0], points[i1], local);
            tangent = points[i1] - points[i0];
            tangent.y = 0f;
            if (tangent.sqrMagnitude < 0.0001f)
            {
                tangent = WorldGenerator.TangentAt(track, i0);
            }
            else
            {
                tangent.Normalize();
            }
        }
    }
}
