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
        public float densityToCountScale = 3.5f;
        [Tooltip("Scattered copies of a landmark type, as a fraction of its normal count. 0 = the landmark appears only as landmarks.")]
        [Range(0f, 1f)] public float landmarkFillerFraction = 0.2f;
        public int landmarkCopies = 2;
        [Tooltip("Horizon copies for a 'scattered' type (keeps the skyline mostly what the VLM called background).")]
        public Vector2Int scatteredHorizonCopies = new Vector2Int(3, 6);

        [Header("Placement (metres from the barrier)")]
        public Vector2 nearBand = new Vector2(1.5f, 5f);
        public Vector2 midBand = new Vector2(6f, 11f);
        [Tooltip("How far out 'background' objects sit -- deliberately just past midBand (which itself now tops out at 11m), not out at horizonBand, so they read as an imposing close wall (mountains, a treeline) that cuts off the view almost immediately instead of a distant skyline. Kept narrow (not a wide band like the others) so consecutive copies along the wall stay at a similar depth and reliably overlap.")]
        public Vector2 backgroundBand = new Vector2(12f, 16f);
        [Tooltip("Metres between background-wall copies along each shoulder, both sides -- deliberately smaller than backgroundScaleRange so consecutive copies overlap continuously instead of leaving gaps.")]
        public float backgroundWallInterval = 4f;
        public Vector2 horizonBand = new Vector2(35f, 65f);
        [Range(0f, 1f)] public float insideLoopChance = 0.35f;
        [Tooltip("Minimum distance a landmark sits off the barrier -- adaptively increased at Spawn time if landmarkScaleRange's footprint needs more room than this (see FootprintFor).")]
        public float landmarkDistance = 4f;
        [Tooltip("Roadside objects sit this far off the barrier.")]
        public Vector2 roadsideBand = new Vector2(1f, 2.5f);
        [Tooltip("Spacing between roadside objects at density 0 and density 1.")]
        public Vector2 roadsideIntervalRange = new Vector2(14f, 5f);

        [Header("Variation")]
        public Vector2 fillerScaleRange = new Vector2(1.6f, 2.6f);
        [Tooltip("Extra scale multiplier for clusters placed in midBand instead of nearBand -- being farther from the road, they need to be bigger to read at the same visual size.")]
        public float midBandScaleBoost = 1.6f;
        public Vector2 roadsideScaleRange = new Vector2(1.8f, 2.4f);
        public Vector2 landmarkScaleRange = new Vector2(6.0f, 9.0f);
        [Tooltip("Floor on a landmark's vertical size specifically, independent of landmarkScaleRange's (uniform) multiplier -- see backgroundMinHeight for the same idea applied to the background wall.")]
        public float landmarkMinHeight = 8f;
        [Tooltip("Large on purpose: at backgroundBand's distance, this is what actually blocks the view instead of just decorating the skyline.")]
        public Vector2 backgroundScaleRange = new Vector2(14f, 20f);
        [Tooltip("Floor on the background wall's vertical size specifically, independent of backgroundScaleRange's (uniform) multiplier -- a retrieved mesh that's naturally wide-but-short would otherwise stay short even at a big scale. Non-uniform on purpose: this is a stylized view-blocking wall, not a proportionally-accurate model.")]
        public float backgroundMinHeight = 16f;
        public Vector2 horizonScaleRange = new Vector2(4f, 6f);
        public float maxTiltDegrees = 4f;
        public Vector2 sinkRange = new Vector2(0.05f, 0.15f);
        public float hueJitter = 0.03f, saturationJitter = 0.10f, valueJitter = 0.15f;

        private const float WallThickness = TrackMeshBuilder.WallThickness;

        private enum Role { Filler, Roadside, Landmark, Horizon, Background }

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
            // Deferred: every "background" type shares ONE continuous wall
            // (see below) so two variants (e.g. "mountain"/"mountain_2" from
            // the backend's retrieval variety, docs/decisions/0009) actually
            // alternate along it, instead of each independently laying down
            // its own full-loop wall that would just double-stack at nearly
            // the same spots.
            var backgroundVariants = new List<(AssetDefinition definition, List<GameObject> instances)>();
            // Grand landmark copies only (not their small scattered-filler
            // fraction) -- used below to clear away anything they end up
            // overlapping, now that landmarks are bigger and closer to the
            // road than the rest of the collision system was tuned for.
            var landmarkInstances = new List<GameObject>();
            foreach (var entry in recipe.objects)
            {
                Debug.Log($"[EnvironmentGenerator] {entry.type} - {entry.placement} - {entry.density}");
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
                        // Same reasoning as the background wall: capping the
                        // swapped-in mesh to the placeholder's own footprint
                        // was silently shrinking a real, wide-but-short GLB
                        // back down below landmarkMinHeight. This bypass is
                        // only safe for the grand copies below, placed with
                        // landmarkDistance's clearance -- it must NOT reach
                        // this type's small landmarkFillerFraction scattered
                        // copies (placed with only nearBand/midBand's much
                        // smaller clearance), or their real mesh could
                        // balloon up to maxHorizontalExtent wide and spill
                        // onto the road. Those get their own separate,
                        // normally-capped AssetDefinition instead of sharing
                        // this one.
                        definition.capMeshToFootprint = false;
                        definition.maxHorizontalExtent = landmarkDistance * 2f;
                        int beforeLandmarks = instances.Count;
                        landmarksPlaced += PlaceLandmarks(layout, rng, definition, landmarksPlaced, instances);
                        for (int i = beforeLandmarks; i < instances.Count; i++) landmarkInstances.Add(instances[i]);

                        int landmarkFillerCount = Mathf.RoundToInt(fillerCount * landmarkFillerFraction);
                        if (landmarkFillerCount > 0)
                        {
                            var fillerDefinition = new AssetDefinition
                            {
                                objectType = entry.type + "_landmark_filler",
                                prefab = definition.prefab,
                                fallbackPrimitive = definition.fallbackPrimitive,
                                tintColor = definition.tintColor,
                                defaultScale = definition.defaultScale,
                                meshTaskId = definition.meshTaskId,
                                // capMeshToFootprint/maxHorizontalExtent stay
                                // at their safe defaults (true / -1) here.
                            };
                            var fillerInstances = new List<GameObject>();
                            definitions.Add(fillerDefinition);
                            placeholdersByType[fillerDefinition.objectType] = fillerInstances;
                            PlaceClusters(layout, rng, fillerDefinition, landmarkFillerCount, zones, fillerInstances);
                        }
                        break;

                    case Placement.Roadside:
                        PlaceRoadside(layout, rng, definition, entry.density, zones, instances);
                        break;

                    case Placement.Background:
                        // The wall deliberately overlaps instead of
                        // reserving exclusive footprint (see PlaceBackgroundWall),
                        // so a retrieved mesh shouldn't be shrunk to fit one.
                        // It still needs a hard backstop, though: a mesh
                        // scaled up to hit backgroundMinHeight can end up far
                        // wider than the placeholder ever was if its native
                        // proportions are wide-but-short, so cap the real
                        // mesh's width independently of height at
                        // backgroundBand's minimum clearance (as a diameter)
                        // -- it can never reach back onto the road no matter
                        // how wide the real asset naturally is.
                        definition.capMeshToFootprint = false;
                        definition.maxHorizontalExtent = backgroundBand.x * 2f;
                        backgroundVariants.Add((definition, instances));
                        break;

                    default: // Scattered
                        PlaceClusters(layout, rng, definition, fillerCount, zones, instances);
                        PlaceHorizon(layout, rng, definition, skyColor, scatteredHorizonCopies, instances);
                        break;
                }
            }

            if (backgroundVariants.Count > 0)
            {
                // A dedicated seed key, not any one variant's -- so adding or
                // removing a variant doesn't reshuffle other types, matching
                // every other placement's determinism rule (docs/decisions/0004).
                var backgroundRng = new WorldRandom(WorldRandom.DeriveSeed(seed, "background_wall"));
                PlaceBackgroundWall(layout, backgroundRng, backgroundVariants);
            }

            if (landmarkInstances.Count > 0)
            {
                RemoveOverlappingWithLandmarks(landmarkInstances, placeholdersByType);
            }

            var config = GameManager.Instance != null ? GameManager.Instance.Config : null;
            if (meshLoader != null && (config == null || config.enableGeneratedMeshes))
            {
                meshLoader.Begin(definitions, placeholdersByType);
            }
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
                float scale = rng.NextRange(landmarkScaleRange.x, landmarkScaleRange.y);
                // A big landmark needs more clearance than landmarkDistance
                // alone guarantees, or its near edge spills back onto the
                // wall/road (see FootprintFor) -- same fix as roadside/clusters.
                float distance = Mathf.Max(landmarkDistance, FootprintFor(definition, scale));
                Vector3 position = layout.Point(index) + outward * (layout.HalfWidth + WallThickness + distance);
                float yaw = Quaternion.LookRotation(-outward, Vector3.up).eulerAngles.y;
                var go = Spawn(definition, position, yaw, scale, Role.Landmark, rng, layout);
                if (go != null)
                {
                    // Parented to the placeholder, not the (possibly later
                    // swapped-in) mesh, so it survives regardless of when or
                    // whether the real mesh ever arrives.
                    LandmarkGlow.Attach(go);
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
        /// Landmarks are now big and close enough to the road that they can
        /// end up overlapping something placed earlier in the generation
        /// loop (collision avoidance only ever protects a NEW placement from
        /// EARLIER ones, never the reverse, so nothing already down when a
        /// landmark was placed knew to leave room for it). Runs once at the
        /// end, after every other placement (including the background wall)
        /// -- clears out anything, of any type, whose rendered bounds
        /// actually intersect a landmark's, so the centrepiece always reads
        /// clean rather than half-buried in whatever was there first.
        /// </summary>
        private static void RemoveOverlappingWithLandmarks(List<GameObject> landmarks, Dictionary<string, List<GameObject>> placeholdersByType)
        {
            foreach (var landmark in landmarks)
            {
                if (landmark == null) continue;
                var landmarkRenderer = landmark.GetComponent<Renderer>();
                if (landmarkRenderer == null) continue;
                Bounds landmarkBounds = landmarkRenderer.bounds;

                foreach (var list in placeholdersByType.Values)
                {
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        var other = list[i];
                        if (other == null || other == landmark) continue;
                        var otherRenderer = other.GetComponent<Renderer>();
                        if (otherRenderer != null && landmarkBounds.Intersects(otherRenderer.bounds))
                        {
                            Destroy(other);
                            list.RemoveAt(i);
                        }
                    }
                }
            }
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
                        // Draw scale before the offset: a big instance needs
                        // more clearance than roadsideBand's minimum alone
                        // guarantees, or its near edge spills back onto the
                        // wall/road (see FootprintFor).
                        float scale = rng.NextRange(roadsideScaleRange.x, roadsideScaleRange.y);
                        float minOffset = Mathf.Max(roadsideBand.x, FootprintFor(definition, scale));
                        float maxOffset = Mathf.Max(minOffset, roadsideBand.y);
                        float across = layout.HalfWidth + WallThickness + rng.NextRange(minOffset, maxOffset);
                        Vector3 position = centre + outward * (across * side);
                        float yaw = Quaternion.LookRotation(-outward * side, Vector3.up).eulerAngles.y;
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
                bool useMidBand = rng.NextFloat() >= 0.7f;
                Vector2 band = useMidBand ? midBand : nearBand;
                float alongSpread = 0.6f * (layout.Point(anchor + 1) - layout.Point(anchor)).magnitude;

                int inCluster = c == clusterCount - 1 ? remaining : Mathf.Min(remaining, rng.NextInt(2, 6));
                for (int i = 0; i < inCluster; i++)
                {
                    remaining--;
                    float scale = rng.NextRange(fillerScaleRange.x, fillerScaleRange.y);
                    if (useMidBand) scale *= midBandScaleBoost;
                    if (rng.NextFloat() < 0.15f) scale *= 1.3f; // the occasional big one
                    // A big instance needs more clearance than band.x alone
                    // guarantees, or its near edge spills back onto the
                    // wall/road (see FootprintFor).
                    float minOffset = Mathf.Max(band.x, FootprintFor(definition, scale));
                    float maxOffset = Mathf.Max(minOffset, band.y);

                    // Up to a few tries to find a free spot; otherwise skip the instance.
                    for (int attempt = 0; attempt < 6; attempt++)
                    {
                        float along = rng.NextRange(-alongSpread, alongSpread);
                        float across = layout.HalfWidth + WallThickness + rng.NextRange(minOffset, maxOffset);
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

        /// <summary>
        /// A continuous wall lining BOTH sides of the whole loop (mountains,
        /// a dense treeline) at backgroundBand -- just past midBand, nowhere
        /// near horizonBand -- so it reads as terrain the road cuts through
        /// rather than a distant skyline. Marches at backgroundWallInterval
        /// like PlaceRoadside, but the interval is deliberately smaller than
        /// backgroundScaleRange so consecutive copies overlap on purpose
        /// (Spawn's Role.Background case skips the IsFree check for exactly
        /// this reason -- every other role treats overlap as a bug). Unlike
        /// PlaceHorizon: covers every zone (a wall with gaps defeats the
        /// point), and no sky-haze tint -- it's close enough that washing it
        /// toward the sky colour would look wrong, so it gets a normal
        /// jittered tint and an outline like everything else in the world.
        /// </summary>
        private void PlaceBackgroundWall(TrackLayout layout, WorldRandom rng, List<(AssetDefinition definition, List<GameObject> instances)> variants)
        {
            float interval = Mathf.Max(2f, backgroundWallInterval);
            float distanceSinceLast = interval;

            for (int i = 0; i < layout.PointCount; i++)
            {
                Vector3 start = layout.Point(i);
                Vector3 segment = layout.Point(i + 1) - start;
                float segmentLength = segment.magnitude;
                Vector3 direction = segment / Mathf.Max(segmentLength, 0.001f);

                float t = interval - distanceSinceLast;
                while (t < segmentLength)
                {
                    Vector3 centre = start + direction * t;
                    Vector3 outward = Vector3.Cross(Vector3.up, direction).normalized;
                    if (Vector3.Dot(outward, layout.Outward(i)) < 0f) outward = -outward;

                    for (int side = -1; side <= 1; side += 2)
                    {
                        // Pick which variant occupies this slot -- this is
                        // what actually mixes two mountain shapes along one
                        // continuous wall, rather than each variant getting
                        // its own full pass over the whole loop.
                        var (definition, instances) = variants[rng.NextInt(0, variants.Count)];
                        float scale = rng.NextRange(backgroundScaleRange.x, backgroundScaleRange.y);
                        float across = layout.HalfWidth + WallThickness + rng.NextRange(backgroundBand.x, backgroundBand.y);
                        Vector3 position = centre + outward * (across * side);
                        var go = Spawn(definition, position, rng.NextRange(0f, 360f), scale, Role.Background, rng, layout);
                        if (go != null) instances.Add(go);
                    }
                    t += interval;
                }
                distanceSinceLast = segmentLength - (t - interval);
            }
        }

        // ------------------------------------------------------------------
        // Instance creation + variation
        // ------------------------------------------------------------------

        /// <summary>
        /// Half the instance's footprint (plus a little clearance), given
        /// its base scale and the role's scale multiplier -- how far an
        /// instance's edge can reach from its center. Shared by Spawn's own
        /// overlap check and by PlaceRoadside/PlaceClusters, which need it
        /// *before* they pick a position: a big instance needs more
        /// clearance from the wall than the placement band's minimum offset
        /// alone guarantees, or its near edge spills back onto the wall or
        /// the road itself instead of just onto whatever's already placed.
        /// </summary>
        private static float FootprintFor(AssetDefinition definition, float scaleMultiplier)
        {
            Vector3 scale = definition.defaultScale * scaleMultiplier;
            // The half-diagonal, not just the larger of the two local axes:
            // PlaceClusters gives every scattered instance a fully random
            // yaw (0-360deg), and a non-square footprint's true world-space
            // AABB at an intermediate angle (worst case: a 45deg-ish
            // rotation on a roughly square footprint) can exceed either
            // local axis alone by up to ~41% -- Max(x,z) alone under-reserved
            // clearance for however the object actually landed rotated,
            // letting its corner clip the wall/road on an unlucky roll.
            // Roadside's yaw is fixed (facing the road), so this is more
            // conservative than strictly needed there, but never wrong.
            float halfDiagonal = 0.5f * Mathf.Sqrt(scale.x * scale.x + scale.z * scale.z);
            return halfDiagonal + 0.5f;
        }

        /// <summary>
        /// Spawn one placeholder resting on the ground at `position`, with
        /// per-instance variation appropriate to its role. Returns null if
        /// the footprint would intersect something already placed.
        /// </summary>
        private GameObject Spawn(AssetDefinition definition, Vector3 position, float yaw, float scaleMultiplier,
            Role role, WorldRandom rng, TrackLayout layout)
        {
            // Callers compute x/z from a control point (which carries the
            // road's elevation) plus horizontal offsets; the height that
            // matters is the ground's at the final spot -- road level on the
            // berm beside the walls, part-way down the embankment further
            // out, the plain beyond it. No random draws, so this never
            // perturbs the placement sequence.
            position.y = layout.GroundHeightAt(position);

            Vector3 scale = definition.defaultScale * scaleMultiplier;
            if (role == Role.Background)
            {
                // Independent of the uniform scaleMultiplier: a naturally
                // wide-but-short definition (or, later, retrieved mesh --
                // see AssetDefinition.capMeshToFootprint) would otherwise
                // stay short even at a big backgroundScaleRange value.
                scale.y = Mathf.Max(scale.y, backgroundMinHeight);
            }
            else if (role == Role.Landmark)
            {
                scale.y = Mathf.Max(scale.y, landmarkMinHeight);
            }
            float footprint = FootprintFor(definition, scaleMultiplier);
            // Horizon and Background are exempt: Horizon is far enough out
            // that collisions are irrelevant, and Background's whole point
            // is a continuous overlapping wall -- see PlaceBackgroundWall.
            if (role != Role.Horizon && role != Role.Background && !layout.IsFree(position, footprint)) return null;

            // Variation draws happen in a fixed order so the sequence stays
            // deterministic regardless of which branch spends them.
            bool organic = role == Role.Filler || role == Role.Horizon || role == Role.Background; // roadside/landmark stay upright and un-mirrored
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
                // Horizon silhouettes stay soft-edged; everything nearer gets
                // the outline.
                renderer.sharedMaterial = ToonStyle.Create(tint, outline: role != Role.Horizon, name: instance.name);
            }

            // Roadside/scattered decoration keeps its (already-solid, from
            // CreatePrimitive) collider as a safety net: if placement ever
            // puts one on the track despite FootprintFor's clearance math,
            // the kart is physically blocked by it instead of clipping
            // through -- it lands on the exact same collision path as
            // hitting the other kart (KartController.OnCollisionEnter
            // already treats "anything solid that isn't a TrackBarrier"
            // uniformly, so this needs no new gameplay code). The collider
            // stays sized to the placeholder even after a real mesh swaps
            // in, since PlaceOver only disables the placeholder's renderer,
            // never destroys the GameObject. Landmark/background/horizon
            // stay pass-through: they're allowed to be big and close by
            // design, not meant to be hit.
            var collider = instance.GetComponent<Collider>();
            if (collider != null && role != Role.Roadside && role != Role.Filler)
            {
                Destroy(collider);
            }

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
