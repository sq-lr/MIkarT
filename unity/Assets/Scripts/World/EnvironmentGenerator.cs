using System.Collections.Generic;
using MarioKart.AI;
using MarioKart.AssetsSystem;
using MarioKart.Core;
using MarioKart.Rendering;
using UnityEngine;

namespace MarioKart.World
{
    /// <summary>
    /// Places WorldRecipe.objects[] around the generated track so it reads
    /// as a composed place rather than the same object stamped every 20 m:
    ///
    ///   • Placement comes from the VLM (recipe objects[].placement -- see
    ///     docs/world-recipe.md), which saw the photo and knows what each
    ///     object *is*:
    ///       landmark   -- the centrepiece: a couple of oversized copies at
    ///                     spots you steer by (opposite the finish, sharpest
    ///                     bend), facing the track, and nothing else.
    ///       roadside   -- lines the road at intervals, both sides, facing it.
    ///       background -- horizon layer only: huge, far, sky-tinted.
    ///       scattered  -- clusters (default when the hint is missing).
    ///   • Water fountains line both shoulders of the course (outside the
    ///     barrier walls, never on the racing lane), independent of the recipe.
    ///   • Zones: the loop is split into arcs and each type is active in only
    ///     some of them, so different stretches of the lap feel different.
    ///   • Clusters: scattered filler goes down in tight groups around a few
    ///     anchors, inside and outside the loop, with empty road between.
    ///   • Per-instance variation: scale, mirror, tilt, sink, tint -- no two
    ///     instances are identical. Minimum spacing keeps them from
    ///     intersecting.
    ///
    /// Deterministic: every random draw comes from a WorldRandom seeded per
    /// object type (docs/decisions/0004), so adding or removing one type
    /// doesn't reshuffle another. Placeholders are primitives spawned
    /// immediately; GeneratedMeshLoader swaps generated meshes in over them
    /// later, adopting each placeholder's transform (so the variation
    /// carries over) without moving anything.
    /// </summary>
    public class EnvironmentGenerator : MonoBehaviour
    {
        // Optional: on the same GameObject (or assigned in the Inspector).
        // Without it, placeholders are simply never replaced.
        [SerializeField] private GeneratedMeshLoader meshLoader;

        /// <summary>The loader swapping generated meshes in, if any.</summary>
        public GeneratedMeshLoader MeshLoader => meshLoader;

        // Interfaces aren't Unity-serializable, so this is wired in code
        // (not the Inspector).
        private readonly IAssetResolver resolver = new AssetResolver();

        [Header("Counts")]
        [Tooltip("density (0-1) × this × control-point count ≈ scattered instances per type.")]
        public float densityToCountScale = 1.5f;
        [Tooltip("Scattered copies of a landmark type, as a fraction of its normal count. 0 = the landmark appears only as landmarks.")]
        [Range(0f, 1f)] public float landmarkFillerFraction = 0f;
        public int landmarkCopies = 2;
        [Tooltip("Horizon copies for a 'background' type.")]
        public Vector2Int backgroundHorizonCopies = new Vector2Int(3, 6);
        [Tooltip("Horizon copies for a 'scattered' type (keeps the skyline mostly what the VLM called background).")]
        public Vector2Int scatteredHorizonCopies = new Vector2Int(0, 1);

        [Header("Placement (metres from the barrier)")]
        public Vector2 nearBand = new Vector2(2f, 7f);
        public Vector2 midBand = new Vector2(9f, 18f);
        public Vector2 horizonBand = new Vector2(80f, 150f);
        [Range(0f, 1f)] public float insideLoopChance = 0.35f;
        public float landmarkDistance = 8f;
        [Tooltip("Roadside objects sit this far off the barrier.")]
        public Vector2 roadsideBand = new Vector2(1f, 3f);
        [Tooltip("Spacing between roadside objects at density 0 and density 1.")]
        public Vector2 roadsideIntervalRange = new Vector2(40f, 12f);

        [Header("Fountains")]
        [Tooltip("Metres between fountains along each shoulder of the course.")]
        public float fountainInterval = 6.5f;
        [Tooltip("How far past the barrier wall the fountains sit (never on the lane).")]
        public Vector2 fountainOffsetBand = new Vector2(2.0f, 4.2f);

        [Header("Variation")]
        public Vector2 fillerScaleRange = new Vector2(0.7f, 1.4f);
        public Vector2 landmarkScaleRange = new Vector2(2.4f, 3.2f);
        public Vector2 horizonScaleRange = new Vector2(5f, 8f);
        public float maxTiltDegrees = 4f;
        public Vector2 sinkRange = new Vector2(0.05f, 0.15f);
        public float hueJitter = 0.03f, saturationJitter = 0.10f, valueJitter = 0.15f;

        private const float WallThickness = 0.5f; // matches TrackMeshBuilder
        private const float FountainStartSkip = 12f;
        private const float FountainFootprint = 1.15f;

        private Material fountainStone;
        private Material fountainWater;
        private Material fountainSpray;

        private enum Role { Filler, Roadside, Landmark, Horizon }

        // Mirrors the schema enum for objects[].placement.
        private enum Placement { Scattered, Roadside, Background, Landmark }

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

            var layout = new TrackLayout(track);
            PlaceFountains(track, layout, seed);

            var definitions = new List<AssetDefinition>();
            var placeholdersByType = new Dictionary<string, List<GameObject>>();

            Color skyColor = GhibliLook.Cream;
            if (recipe.palette != null && recipe.palette.Count > 2)
            {
                ColorUtility.TryParseHtmlString(recipe.palette[2], out skyColor);
                skyColor = Color.Lerp(skyColor, GhibliLook.Cream, 0.45f);
            }

            int typeCount = recipe.objects.Count;
            int landmarksPlaced = 0; // shared so a second landmark type takes the next spot
            foreach (var entry in recipe.objects)
            {
                var rng = new WorldRandom(WorldRandom.DeriveSeed(seed, entry.type));
                var definition = resolver.Resolve(entry);
                definitions.Add(definition);

                var instances = new List<GameObject>();
                placeholdersByType[entry.type] = instances;

                int fillerCount = Mathf.RoundToInt(entry.density * densityToCountScale * layout.PointCount);
                // A single type gets the whole loop; otherwise 2-3 of the zones.
                var zones = PickZones(rng, typeCount > 1 ? rng.NextInt(2, TrackLayout.ZoneCount) : TrackLayout.ZoneCount);

                switch (ParsePlacement(entry))
                {
                    case Placement.Landmark:
                        landmarksPlaced += PlaceLandmarks(layout, rng, definition, landmarksPlaced, instances);
                        PlaceClusters(layout, rng, definition, Mathf.RoundToInt(fillerCount * landmarkFillerFraction), zones, instances);
                        break;

                    case Placement.Roadside:
                        PlaceRoadside(layout, rng, definition, entry.density, zones, instances);
                        break;

                    case Placement.Background:
                        PlaceHorizon(layout, rng, definition, skyColor, backgroundHorizonCopies, instances);
                        break;

                    default: // Scattered
                        PlaceClusters(layout, rng, definition, fillerCount, zones, instances);
                        PlaceHorizon(layout, rng, definition, skyColor, scatteredHorizonCopies, instances);
                        break;
                }
            }

            var config = GameManager.Instance != null ? GameManager.Instance.Config : null;
            if (meshLoader != null && (config == null || config.enableGeneratedMeshes))
            {
                meshLoader.Begin(definitions, placeholdersByType);
            }
        }

        /// <summary>
        /// Dense row of water fountains on both shoulders of the smoothed
        /// racing line, always outside the barrier walls so they never sit
        /// in the lane. Placed before recipe props so trees fill around them.
        /// </summary>
        private void PlaceFountains(GeneratedTrack track, TrackLayout layout, int seed)
        {
            if (track?.controlPoints == null || track.controlPoints.Count < 2) return;

            EnsureFountainMaterials();
            var rng = new WorldRandom(WorldRandom.DeriveSeed(seed, "fountains"));
            var lane = TrackMeshBuilder.SmoothLoop(track.controlPoints);
            int n = lane.Count;
            if (n < 2) return;

            float loopLength = 0f;
            for (int i = 0; i < n; i++)
            {
                loopLength += Vector3.Distance(lane[i], lane[(i + 1) % n]);
            }

            float interval = Mathf.Max(3.5f, fountainInterval);
            float distance = 0f;
            float sinceLast = interval;

            for (int i = 0; i < n; i++)
            {
                Vector3 start = lane[i];
                Vector3 segment = lane[(i + 1) % n] - start;
                float segmentLength = segment.magnitude;
                Vector3 direction = segment / Mathf.Max(segmentLength, 0.001f);
                Vector3 across = Vector3.Cross(Vector3.up, direction);
                if (across.sqrMagnitude < 0.0001f) across = Vector3.right;
                across.Normalize();

                float t = interval - sinceLast;
                while (t < segmentLength)
                {
                    float along = distance + t;
                    float fromStart = Mathf.Min(along, loopLength - along);
                    if (fromStart >= FountainStartSkip)
                    {
                        Vector3 centre = start + direction * t;
                        for (int side = -1; side <= 1; side += 2)
                        {
                            TrySpawnFountain(layout, rng, centre, across * side);
                        }
                    }
                    t += interval;
                }
                sinceLast = segmentLength - (t - interval);
                distance += segmentLength;
            }
        }

        private void TrySpawnFountain(TrackLayout layout, WorldRandom rng, Vector3 centre, Vector3 outward)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                float extra = rng.NextRange(fountainOffsetBand.x, fountainOffsetBand.y) + attempt * 1.1f;
                Vector3 position = centre + outward * (layout.HalfWidth + WallThickness + extra);
                position.y = 0f;
                if (!layout.IsFree(position, FountainFootprint)) continue;

                float scale = rng.NextRange(0.85f, 1.25f);
                SpawnFountain(position, Quaternion.LookRotation(-outward, Vector3.up), scale);
                layout.Occupy(position, FountainFootprint * scale);
                return;
            }
        }

        private void SpawnFountain(Vector3 position, Quaternion rotation, float scale)
        {
            var root = new GameObject("Fountain");
            root.transform.SetParent(transform, worldPositionStays: false);
            root.transform.SetPositionAndRotation(position, rotation);
            root.transform.localScale = Vector3.one * scale;

            AddPart(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.16f, 0f), new Vector3(1.85f, 0.16f, 1.85f), fountainStone);
            AddPart(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.62f, 0f), new Vector3(0.38f, 0.46f, 0.38f), fountainStone);
            AddPart(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 1.18f, 0f), new Vector3(1.15f, 0.12f, 1.15f), fountainStone);
            AddPart(root.transform, PrimitiveType.Sphere, new Vector3(0f, 1.32f, 0f), new Vector3(1.02f, 0.10f, 1.02f), fountainWater);
            AddPart(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 1.85f, 0f), new Vector3(0.14f, 0.52f, 0.14f), fountainWater);
            AddPart(root.transform, PrimitiveType.Sphere, new Vector3(0f, 2.42f, 0f), new Vector3(0.48f, 0.38f, 0.48f), fountainWater);
            AddSpray(root.transform);
        }

        private static void AddPart(Transform parent, PrimitiveType type, Vector3 localPosition, Vector3 localScale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        private void AddSpray(Transform parent)
        {
            var go = new GameObject("Spray");
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = new Vector3(0f, 2.05f, 0f);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.duration = 1f;
            main.startLifetime = 0.65f;
            main.startSpeed = 2.4f;
            main.startSize = 0.11f;
            main.startColor = new Color(0.78f, 0.92f, 0.96f, 0.7f);
            main.gravityModifier = 1.15f;
            main.maxParticles = 18;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = ps.emission;
            emission.rateOverTime = 14f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.04f;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.7f, 0.88f, 0.92f), 1f) },
                new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0.35f, 0.45f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = fountainSpray;
        }

        private void EnsureFountainMaterials()
        {
            if (fountainStone == null)
            {
                fountainStone = GhibliLook.Lit(new Color(0.78f, 0.74f, 0.64f));
                fountainStone.name = "FountainStone";
            }
            if (fountainWater == null)
            {
                fountainWater = GhibliLook.Lit(new Color(0.52f, 0.80f, 0.86f));
                fountainWater.name = "FountainWater";
                if (fountainWater.HasProperty("_Fill")) fountainWater.SetFloat("_Fill", 0.34f);
            }
            if (fountainSpray == null)
            {
                var shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Sprites/Default");
                fountainSpray = new Material(shader) { name = "FountainSpray", color = new Color(0.85f, 0.95f, 1f, 0.65f) };
            }
        }

        private void OnDestroy()
        {
            if (fountainStone != null) Destroy(fountainStone);
            if (fountainWater != null) Destroy(fountainWater);
            if (fountainSpray != null) Destroy(fountainSpray);
        }

        // ------------------------------------------------------------------
        // Placement strategies
        // ------------------------------------------------------------------

        private static Placement ParsePlacement(WorldObjectEntry entry)
        {
            switch (entry.placement)
            {
                case null:
                case "":
                case "scattered": return Placement.Scattered;
                case "roadside": return Placement.Roadside;
                case "background": return Placement.Background;
                case "landmark": return Placement.Landmark;
                default:
                    Debug.LogWarning($"EnvironmentGenerator: unknown placement '{entry.placement}' for '{entry.type}'; treating as scattered");
                    return Placement.Scattered;
            }
        }

        private HashSet<int> PickZones(WorldRandom rng, int count)
        {
            var all = new List<int>();
            for (int z = 0; z < TrackLayout.ZoneCount; z++) all.Add(z);
            var picked = new HashSet<int>();
            count = Mathf.Clamp(count, 1, TrackLayout.ZoneCount);
            while (picked.Count < count)
            {
                picked.Add(all[rng.NextInt(0, all.Count)]);
            }
            return picked;
        }

        /// <summary>
        /// Oversized copies of a landmark type at spots you steer by:
        /// opposite the start/finish, then the sharpest bend, then evenly
        /// spaced. `firstSpot` lets a second landmark type (the backend
        /// allows one, but be defensive) continue down the list instead of
        /// stacking on the same spot. Returns how many were placed.
        /// </summary>
        private int PlaceLandmarks(TrackLayout layout, WorldRandom rng, AssetDefinition definition, int firstSpot, List<GameObject> instances)
        {
            int placed = 0;
            for (int i = firstSpot; i < firstSpot + landmarkCopies; i++)
            {
                int index = LandmarkSpot(layout, i);
                Vector3 outward = layout.Outward(index);
                Vector3 position = layout.Point(index) + outward * (layout.HalfWidth + WallThickness + landmarkDistance);
                float scale = rng.NextRange(landmarkScaleRange.x, landmarkScaleRange.y);
                float yaw = Quaternion.LookRotation(-outward, Vector3.up).eulerAngles.y;
                var go = Spawn(definition, position, yaw, scale, Role.Landmark, rng, layout);
                if (go != null)
                {
                    instances.Add(go);
                    placed++;
                }
            }
            return placed;
        }

        private static int LandmarkSpot(TrackLayout layout, int ordinal)
        {
            int opposite = layout.Wrap(layout.PointCount / 2);
            if (ordinal == 0) return opposite;
            if (ordinal == 1) return layout.SharpestBend(avoidIndex: opposite, avoidRadius: 3);
            // Further spots: spread around the loop, avoiding the start line.
            return layout.Wrap(layout.PointCount * (2 * ordinal - 1) / (2 * (ordinal + 1)));
        }

        /// <summary>
        /// Line the road at a regular interval (denser types closer together)
        /// through the type's zones, both sides, every instance facing the
        /// road. Uniform scale and no tilt: these are usually man-made.
        /// </summary>
        private void PlaceRoadside(TrackLayout layout, WorldRandom rng, AssetDefinition definition, float density,
            HashSet<int> zones, List<GameObject> instances)
        {
            float interval = Mathf.Lerp(roadsideIntervalRange.x, roadsideIntervalRange.y, Mathf.Clamp01(density));
            float distanceSinceLast = interval; // place one right at the first eligible point

            for (int i = 0; i < layout.PointCount; i++)
            {
                if (!zones.Contains(layout.ZoneOf(i)) || layout.CircularDistance(i, 0) <= 1)
                {
                    distanceSinceLast = interval;
                    continue;
                }

                Vector3 start = layout.Point(i);
                Vector3 segment = layout.Point(i + 1) - start;
                float segmentLength = segment.magnitude;
                Vector3 direction = segment / Mathf.Max(segmentLength, 0.001f);

                // March along this control-point segment dropping instances
                // every `interval` metres, carrying the remainder over.
                float t = interval - distanceSinceLast;
                while (t < segmentLength)
                {
                    Vector3 centre = start + direction * t;
                    Vector3 outward = Vector3.Cross(Vector3.up, direction).normalized;
                    if (Vector3.Dot(outward, layout.Outward(i)) < 0f) outward = -outward;

                    for (int side = -1; side <= 1; side += 2)
                    {
                        float across = layout.HalfWidth + WallThickness + rng.NextRange(roadsideBand.x, roadsideBand.y);
                        Vector3 position = centre + outward * (across * side);
                        float yaw = Quaternion.LookRotation(-outward * side, Vector3.up).eulerAngles.y;
                        float scale = rng.NextRange(0.9f, 1.1f);
                        var go = Spawn(definition, position, yaw, scale, Role.Roadside, rng, layout);
                        if (go != null) instances.Add(go);
                    }
                    t += interval;
                }
                distanceSinceLast = segmentLength - (t - interval);
            }
        }

        /// <summary>
        /// Filler in tight groups around a few anchors within the type's
        /// zones. Each cluster picks a side of the track and a distance
        /// band; instances scatter around the anchor along and across the
        /// track, skipping spots that would intersect something.
        /// </summary>
        private void PlaceClusters(TrackLayout layout, WorldRandom rng, AssetDefinition definition, int count,
            HashSet<int> zones, List<GameObject> instances)
        {
            if (count <= 0) return;

            var candidates = layout.IndicesInZones(zones, excludeNear: 0, excludeRadius: 1);
            if (candidates.Count == 0) return;

            int clusterCount = Mathf.Clamp(Mathf.RoundToInt(count / 3f), 1, 6);
            int remaining = count;

            for (int c = 0; c < clusterCount && remaining > 0; c++)
            {
                int anchor = candidates[rng.NextInt(0, candidates.Count)];
                float side = rng.NextFloat() < insideLoopChance ? -1f : 1f;
                Vector2 band = rng.NextFloat() < 0.7f ? nearBand : midBand;
                float alongSpread = 0.6f * (layout.Point(anchor + 1) - layout.Point(anchor)).magnitude;

                int inCluster = c == clusterCount - 1 ? remaining : Mathf.Min(remaining, rng.NextInt(2, 6));
                for (int i = 0; i < inCluster; i++)
                {
                    remaining--;
                    float scale = rng.NextRange(fillerScaleRange.x, fillerScaleRange.y);
                    if (rng.NextFloat() < 0.15f) scale *= 1.3f; // the occasional big one

                    // Up to a few tries to find a free spot; otherwise skip the instance.
                    for (int attempt = 0; attempt < 6; attempt++)
                    {
                        float along = rng.NextRange(-alongSpread, alongSpread);
                        float across = layout.HalfWidth + WallThickness + rng.NextRange(band.x, band.y);
                        Vector3 position = layout.Point(anchor) + layout.Tangent(anchor) * along + layout.Outward(anchor) * (across * side);

                        var go = Spawn(definition, position, rng.NextRange(0f, 360f), scale, Role.Filler, rng, layout);
                        if (go != null)
                        {
                            instances.Add(go);
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>Huge, sky-tinted silhouettes far outside the loop.</summary>
        private void PlaceHorizon(TrackLayout layout, WorldRandom rng, AssetDefinition definition, Color skyColor,
            Vector2Int copiesRange, List<GameObject> instances)
        {
            int copies = rng.NextInt(copiesRange.x, copiesRange.y + 1);
            for (int i = 0; i < copies; i++)
            {
                int index = rng.NextInt(0, layout.PointCount);
                float distance = rng.NextRange(horizonBand.x, horizonBand.y);
                Vector3 position = layout.Point(index) + layout.Outward(index) * distance;
                float scale = rng.NextRange(horizonScaleRange.x, horizonScaleRange.y);

                var go = Spawn(definition, position, rng.NextRange(0f, 360f), scale, Role.Horizon, rng, layout);
                if (go == null) continue;
                instances.Add(go);

                var renderer = go.GetComponent<Renderer>();
                if (renderer != null && definition.prefab == null)
                {
                    Color haze = Color.Lerp(renderer.sharedMaterial.color, skyColor, 0.6f);
                    renderer.sharedMaterial = GhibliLook.Lit(haze);
                    if (renderer.sharedMaterial.HasProperty("_Fill"))
                    {
                        renderer.sharedMaterial.SetFloat("_Fill", 0.28f);
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // Instance creation + variation
        // ------------------------------------------------------------------

        /// <summary>
        /// Spawn one placeholder resting on the ground at `position`, with
        /// per-instance variation appropriate to its role. Returns null if
        /// the footprint would intersect something already placed.
        /// </summary>
        private GameObject Spawn(AssetDefinition definition, Vector3 position, float yaw, float scaleMultiplier,
            Role role, WorldRandom rng, TrackLayout layout)
        {
            Vector3 scale = definition.defaultScale * scaleMultiplier;
            float footprint = Mathf.Max(scale.x, scale.z) * 0.5f + 0.5f;
            if (role != Role.Horizon && !layout.IsFree(position, footprint)) return null;

            // Variation draws happen in a fixed order so the sequence stays
            // deterministic regardless of which branch spends them.
            bool organic = role == Role.Filler || role == Role.Horizon; // roadside/landmark stay upright and un-mirrored
            bool mirror = organic && rng.NextFloat() < 0.5f;
            float tiltX = role == Role.Filler ? rng.NextRange(-maxTiltDegrees, maxTiltDegrees) : 0f;
            float tiltZ = role == Role.Filler ? rng.NextRange(-maxTiltDegrees, maxTiltDegrees) : 0f;
            float sink = rng.NextRange(sinkRange.x, sinkRange.y);
            float hue = rng.NextRange(-hueJitter, hueJitter);
            float sat = rng.NextRange(-saturationJitter, saturationJitter);
            float val = rng.NextRange(-valueJitter, valueJitter);

            GameObject instance = definition.prefab != null
                ? Instantiate(definition.prefab)
                : GameObject.CreatePrimitive(definition.fallbackPrimitive);

            instance.name = $"{definition.objectType}_Placeholder";
            instance.transform.SetParent(transform, worldPositionStays: false);
            instance.transform.rotation = Quaternion.Euler(tiltX, yaw, tiltZ);
            if (mirror) scale.x = -scale.x;
            instance.transform.localScale = scale;

            // Rest the bottom of the rendered bounds on the ground (minus a
            // little sink), instead of centring the primitive on it.
            instance.transform.position = position;
            var renderer = instance.GetComponent<Renderer>();
            if (renderer != null)
            {
                float lift = position.y - renderer.bounds.min.y;
                instance.transform.position = position + Vector3.up * (lift - sink);
            }

            if (definition.prefab == null && renderer != null)
            {
                Color tint = role == Role.Landmark
                    ? definition.tintColor
                    : JitterColor(definition.tintColor, hue, sat, val);
                renderer.sharedMaterial = GhibliLook.Lit(tint);
            }

            // Decoration only: karts are kept on the road by the barriers.
            var collider = instance.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            layout.Occupy(position, footprint);
            return instance;
        }

        private static Color JitterColor(Color color, float hueDelta, float satDelta, float valDelta)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);
            h = Mathf.Repeat(h + hueDelta, 1f);
            s = Mathf.Clamp01(s + satDelta);
            v = Mathf.Clamp01(v + valDelta);
            var result = Color.HSVToRGB(h, s, v);
            result.a = color.a;
            return result;
        }
    }
}
