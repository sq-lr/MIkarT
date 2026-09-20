using System.Collections.Generic;
using UnityEngine;

namespace MarioKart.World
{
    /// <summary>
    /// Track-derived geometry the environment placer needs: zones (arcs of
    /// the loop), landmark spots, outward direction, and a spacing registry
    /// so instances don't intersect. Pure functions of the GeneratedTrack --
    /// no randomness -- so the same track always yields the same layout.
    /// </summary>
    public class TrackLayout
    {
        public const int ZoneCount = 4;

        private readonly GeneratedTrack track;
        private readonly List<(Vector3 position, float radius)> occupied = new List<(Vector3, float)>();

        public int PointCount => track.controlPoints.Count;
        public float HalfWidth => track.width * 0.5f;

        public TrackLayout(GeneratedTrack track)
        {
            this.track = track;
        }

        public Vector3 Point(int index) => track.controlPoints[Wrap(index)];

        /// <summary>Height an object at `position` rests on (road, embankment, or the plain) -- see GeneratedTrack.GroundHeightAt.</summary>
        public float GroundHeightAt(Vector3 position) => track.GroundHeightAt(position);

        public Vector3 Tangent(int index) => WorldGenerator.TangentAt(track, Wrap(index));

        /// <summary>
        /// Away from the loop's interior. The loop is a perturbed circle
        /// around the origin, so radial-from-origin is a good "outside".
        /// </summary>
        public Vector3 Outward(int index)
        {
            var p = Point(index);
            p.y = 0f;
            return p.sqrMagnitude > 0.001f ? p.normalized : Vector3.right;
        }

        /// <summary>Which of the ZoneCount equal arcs a control point falls in.</summary>
        public int ZoneOf(int index) => Wrap(index) * ZoneCount / PointCount;

        public List<int> IndicesInZones(ICollection<int> zones, int excludeNear = -1, int excludeRadius = 1)
        {
            var result = new List<int>();
            for (int i = 0; i < PointCount; i++)
            {
                if (!zones.Contains(ZoneOf(i))) continue;
                if (excludeNear >= 0 && CircularDistance(i, excludeNear) <= excludeRadius) continue;
                result.Add(i);
            }
            return result;
        }

        /// <summary>
        /// Control point with the tightest bend (largest heading change),
        /// ignoring anything within `avoidRadius` points of `avoidIndex`.
        /// </summary>
        public int SharpestBend(int avoidIndex = -1, int avoidRadius = 2)
        {
            int best = -1;
            float bestTurn = -1f;
            for (int i = 0; i < PointCount; i++)
            {
                if (avoidIndex >= 0 && CircularDistance(i, avoidIndex) <= avoidRadius) continue;
                Vector3 inDir = (Point(i) - Point(i - 1)).normalized;
                Vector3 outDir = (Point(i + 1) - Point(i)).normalized;
                float turn = Vector3.Angle(inDir, outDir);
                if (turn > bestTurn)
                {
                    bestTurn = turn;
                    best = i;
                }
            }
            return best < 0 ? Wrap(PointCount / 4) : best;
        }

        public int CircularDistance(int a, int b)
        {
            int d = Mathf.Abs(Wrap(a) - Wrap(b));
            return Mathf.Min(d, PointCount - d);
        }

        public int Wrap(int index) => ((index % PointCount) + PointCount) % PointCount;

        // ---- Spacing ---------------------------------------------------------

        /// <summary>True if a footprint of `radius` at `position` overlaps nothing placed so far.</summary>
        public bool IsFree(Vector3 position, float radius)
        {
            foreach (var (other, otherRadius) in occupied)
            {
                float minDistance = radius + otherRadius;
                Vector3 delta = position - other;
                delta.y = 0f;
                if (delta.sqrMagnitude < minDistance * minDistance) return false;
            }
            return true;
        }

        public void Occupy(Vector3 position, float radius)
        {
            occupied.Add((position, radius));
        }
    }
}
