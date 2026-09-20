using System.Collections;
using System.Collections.Generic;
using MarioKart.Rendering;
using UnityEngine;

namespace MarioKart.World
{
    /// <summary>
    /// Drops pass-through pickups on the racing line. Placement is random
    /// every Generate() call (a time salt is mixed into the derived seed) so
    /// a new game — or Play Again — reshuffles them. Positions are sampled
    /// on the same smoothed loop as the road mesh, inset from the barrier
    /// walls, so pickups stay in the lane. Collecting one destroys it and
    /// spawns a replacement elsewhere.
    /// </summary>
    public class ObstacleGenerator : MonoBehaviour
    {
        private const int Count = 12;
        private const float StartSkip = 0.1f;
        // Stay inside the asphalt: walls sit at ±width/2, pickups have a
        // ~0.55 m trigger, so leave that plus a little extra off each edge.
        private const float EdgeClearance = 1.1f;
        private const float Height = 0.7f;
        private const float PickupScale = 0.9f;
        private const float TriggerRadius = 0.55f;
        private const float RespawnDelay = 0.45f;
        private const float MinRespawnDistance = 14f;

        private GeneratedTrack track;
        private List<Vector3> lane;
        private WorldRandom rng;

        public void Generate(GeneratedTrack generatedTrack, int recipeSeed)
        {
            StopAllCoroutines();
            ClearChildren();

            track = generatedTrack;
            if (track == null || track.controlPoints == null || track.controlPoints.Count < 2) return;

            lane = TrackMeshBuilder.SmoothLoop(track.controlPoints);

            int salt = unchecked(System.Environment.TickCount);
            int seed = WorldRandom.DeriveSeed(WorldRandom.DeriveSeed(recipeSeed, "obstacles"), salt.ToString());
            rng = new WorldRandom(seed);

            var kinds = MixKinds();
            for (int i = 0; i < Count; i++)
            {
                Spawn(kinds[i], RandomPose());
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
            if (track == null || lane == null || rng == null) yield break;
            Spawn((ObstacleKind)rng.NextInt(0, 3), RandomPose(MinRespawnDistance));
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

        private GameObject Spawn(ObstacleKind kind, Pose pose)
        {
            var go = new GameObject($"Obstacle_{kind}");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);

            var trigger = go.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = TriggerRadius;

            BuildVisual(go.transform, kind);

            var obstacle = go.AddComponent<TrackObstacle>();
            obstacle.Configure(kind, rng.NextFloat() * Mathf.PI * 2f, this);
            return go;
        }

        private static void BuildVisual(Transform root, ObstacleKind kind)
        {
            Color color = TrackObstacle.KindColor(kind);
            GameObject visual;
            switch (kind)
            {
                case ObstacleKind.Boost:
                    visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    visual.transform.localScale = new Vector3(0.55f, 0.55f, 0.55f) * PickupScale;
                    visual.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
                    break;
                case ObstacleKind.Paralyze:
                    visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    visual.transform.localScale = Vector3.one * (PickupScale * 0.75f);
                    break;
                default:
                    visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    visual.transform.localScale = new Vector3(PickupScale, PickupScale * 0.22f, PickupScale);
                    break;
            }

            visual.name = "Visual";
            visual.transform.SetParent(root, worldPositionStays: false);
            visual.transform.localPosition = Vector3.zero;
            var visualCollider = visual.GetComponent<Collider>();
            if (visualCollider != null) Destroy(visualCollider);

            var renderer = visual.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = GhibliLook.Lit(color);
            }
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
            t = Mathf.Repeat(t, 1f);
            SampleLane(lane, t, out Vector3 center, out Vector3 tangent);
            Vector3 across = Vector3.Cross(Vector3.up, tangent);
            if (across.sqrMagnitude < 0.0001f) across = Vector3.right;
            across.Normalize();

            float maxLateral = Mathf.Max(0.2f, track.width * 0.5f - EdgeClearance);
            float lateral = rng.NextRange(-maxLateral, maxLateral);
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

        private static void SampleLane(List<Vector3> points, float t, out Vector3 position, out Vector3 tangent)
        {
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
                Vector3 prev = points[(i0 - 1 + n) % n];
                Vector3 next = points[(i0 + 1) % n];
                tangent = next - prev;
                tangent.y = 0f;
            }
            if (tangent.sqrMagnitude > 0.0001f) tangent.Normalize();
            else tangent = Vector3.forward;
        }
    }
}
