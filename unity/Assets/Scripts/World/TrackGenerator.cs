using System.Collections.Generic;
using MarioKart.AI;
using UnityEngine;

namespace MarioKart.World
{
    /// <summary>
    /// Output of TrackGenerator.Generate. Not given its own file in the
    /// original listing -- kept alongside its producer.
    ///
    /// Control points carry elevation (y). The helpers below answer "how
    /// high is the world here?" for everything that has to sit on the
    /// track or beside it: the start grid, the checkpoints, and every
    /// environment object (see EnvironmentGenerator.Spawn).
    /// </summary>
    public class GeneratedTrack
    {
        public List<Vector3> controlPoints;
        public List<Vector3> checkpointPositions;
        public float width;

        /// <summary>
        /// Rise per metre of the embankment that carries a raised road down
        /// to ground level (1:2). Shared by the mesh builder (which draws it)
        /// and GroundHeightAt (which rests objects on it).
        /// </summary>
        public const float ApronGrade = 0.5f;

        /// <summary>Ground level away from the track (the flat plane objects rested on before elevation existed).</summary>
        public const float GroundLevel = 0f;

        private List<Vector3> samples;

        /// <summary>The smoothed road centreline (192 samples), cached.</summary>
        public List<Vector3> Samples => samples ??= TrackMeshBuilder.SmoothLoop(controlPoints);

        /// <summary>Index of the centreline sample horizontally nearest to `position`.</summary>
        public int NearestSample(Vector3 position)
        {
            var s = Samples;
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < s.Count; i++)
            {
                float dx = s[i].x - position.x, dz = s[i].z - position.z;
                float d = dx * dx + dz * dz;
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            return best;
        }

        /// <summary>Direction of travel at centreline sample `i`, including slope (unit length).</summary>
        public Vector3 SampleTangent(int i)
        {
            var s = Samples;
            int n = s.Count;
            Vector3 t = s[(i + 1) % n] - s[(i - 1 + n) % n];
            return t.sqrMagnitude > 0f ? t.normalized : Vector3.forward;
        }

        /// <summary>
        /// Closest point on the centreline polyline to `position`
        /// (horizontally), with the centreline's height interpolated along
        /// the segment -- the same linear interpolation the road ribbon is
        /// built with, so anything derived from this sits exactly where the
        /// road does. Snapping to the nearest *sample* instead is off by up
        /// to half a sample of slope (metres on a steep hill).
        /// </summary>
        /// <returns>Perpendicular horizontal distance from the centreline.</returns>
        public float ProjectOntoCentreline(Vector3 position, out float centreHeight)
        {
            var s = Samples;
            int n = s.Count;
            int i = NearestSample(position);
            var p = new Vector2(position.x, position.z);

            // The closest point lies on one of the two segments touching the
            // nearest sample.
            float best = float.MaxValue;
            centreHeight = s[i].y;
            for (int k = -1; k <= 0; k++)
            {
                Vector3 a = s[(i + k + n) % n];
                Vector3 b = s[(i + k + 1) % n];
                var a2 = new Vector2(a.x, a.z);
                var ab = new Vector2(b.x, b.z) - a2;
                float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a2, ab) / ab.sqrMagnitude) : 0f;
                float d = (p - (a2 + ab * t)).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    centreHeight = Mathf.Lerp(a.y, b.y, t);
                }
            }
            return Mathf.Sqrt(best);
        }

        /// <summary>Height of the driveable road surface nearest to `position`.</summary>
        public float RoadHeightAt(Vector3 position)
        {
            ProjectOntoCentreline(position, out float centreHeight);
            return centreHeight + TrackMeshBuilder.RoadHeight;
        }

        /// <summary>
        /// Height of the ground an object at `position` should rest on: the
        /// road's height between the walls, the embankment's height across
        /// the berm outside them, and GroundLevel once the berm has reached
        /// the plain.
        /// </summary>
        public float GroundHeightAt(Vector3 position) => GroundHeightAt(position, out _);

        /// <summary>As above, also returning the horizontal distance from the road centreline.</summary>
        public float GroundHeightAt(Vector3 position, out float lateral)
        {
            lateral = ProjectOntoCentreline(position, out float centreHeight);
            float edge = width * 0.5f + TrackMeshBuilder.WallThickness;
            float height = lateral <= edge ? centreHeight : centreHeight - (lateral - edge) * ApronGrade;
            return Mathf.Max(GroundLevel, height);
        }

        /// <summary>
        /// Furthest the embankment can reach from the centreline anywhere on
        /// the loop -- how far out the terrain mesh has to extend.
        /// </summary>
        public float MaxApronReach()
        {
            float highest = 0f;
            foreach (var p in Samples) highest = Mathf.Max(highest, p.y);
            return width * 0.5f + TrackMeshBuilder.WallThickness + highest / ApronGrade;
        }

        /// <summary>
        /// Road pose at `position`: surface height plus the slope-following
        /// forward direction and surface normal, for placing a kart flush on
        /// a hill before physics has had a chance to settle it.
        /// </summary>
        public float SurfaceFrameAt(Vector3 position, out Vector3 forward, out Vector3 normal)
        {
            int i = NearestSample(position);
            forward = SampleTangent(i);
            Vector3 flat = new Vector3(forward.x, 0f, forward.z).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, flat);
            normal = Vector3.Cross(forward, right).normalized; // road is flat across, so only the along-slope tilts it
            if (normal.y < 0f) normal = -normal;
            return RoadHeightAt(position);
        }
    }

    /// <summary>
    /// Builds ONE simple loop track from a WorldRecipe. Deterministic: the
    /// same recipe + seed always produces the same control points and
    /// checkpoints. No multi-template, branching, or jump support -- that is
    /// explicitly out of scope.
    ///
    /// Elevation: the loop rolls over a few broad hills (three harmonics
    /// around the loop with random phases) and has one sharper drop, both
    /// scaled by recipe.track.difficulty -- 0 is the old flat track. The
    /// steepest grade is capped so a short loop never turns into a ramp.
    /// The elevation draws come *after* the radius draws so the x/z layout
    /// for a given seed is identical to the flat version.
    /// </summary>
    public class TrackGenerator
    {
        private const int ControlPointCount = 24;
        private const int CheckpointStride = 3; // 24 / 8 checkpoints

        private const float MaxHillHeight = 30f;  // hill amplitude (m) at difficulty 1; the harmonics sum to ±this
        private const float MaxDropHeight = 12f;  // metres lost over the drop at difficulty 1
        private const float DropLengthSegments = 1.5f; // control-point segments the drop spans
        private const float MaxGrade = 0.5f;      // rise / run (~27°), hills and drop each -- arcade-steep
        private static readonly float[] HarmonicWeights = { 0.5f, 0.3f, 0.2f };

        public GeneratedTrack Generate(WorldRecipe recipe, int seed)
        {
            var rng = new WorldRandom(seed);
            float radius = recipe.track.length / (2f * Mathf.PI);
            float maxPerturbation = recipe.track.difficulty * radius * 0.15f;

            var controlPoints = new List<Vector3>(ControlPointCount);
            var checkpoints = new List<Vector3>();

            for (int i = 0; i < ControlPointCount; i++)
            {
                float angle = (i / (float)ControlPointCount) * Mathf.PI * 2f;
                float perturbedRadius = radius + rng.NextRange(-maxPerturbation, maxPerturbation);

                var point = new Vector3(
                    Mathf.Cos(angle) * perturbedRadius,
                    0f,
                    Mathf.Sin(angle) * perturbedRadius);

                controlPoints.Add(point);
            }

            ApplyElevation(controlPoints, rng, radius, recipe.track.difficulty);

            for (int i = 0; i < ControlPointCount; i += CheckpointStride)
            {
                checkpoints.Add(controlPoints[i]);
            }

            return new GeneratedTrack
            {
                controlPoints = controlPoints,
                checkpointPositions = checkpoints,
                width = recipe.track.width,
            };
        }

        private static void ApplyElevation(List<Vector3> points, WorldRandom rng, float radius, float difficulty)
        {
            int n = points.Count;
            float loopLength = 2f * Mathf.PI * radius;

            // Hills: Σ A_k sin(kθ + φ_k). Its steepest grade is amp × Σ(w_k k) / radius.
            float weightedHarmonics = 0f;
            for (int k = 0; k < HarmonicWeights.Length; k++) weightedHarmonics += HarmonicWeights[k] * (k + 1);
            float hillAmplitude = Mathf.Min(MaxHillHeight * difficulty, MaxGrade * radius / weightedHarmonics);
            var phases = new float[HarmonicWeights.Length];
            for (int k = 0; k < phases.Length; k++) phases[k] = rng.NextRange(0f, Mathf.PI * 2f);

            // Drop: a sawtooth around the loop -- one short, steep descent,
            // then a gentle climb back over the rest of the lap. Kept away
            // from the start/finish so the grid and finish strip stay flat-ish.
            float dropFraction = DropLengthSegments / n;
            float dropRun = dropFraction * loopLength;
            float dropHeight = Mathf.Min(MaxDropHeight * difficulty, MaxGrade * dropRun / (1f - dropFraction));
            int dropStart = rng.NextInt(3, n - 3);

            var heights = new float[n];
            for (int i = 0; i < n; i++)
            {
                float theta = (i / (float)n) * Mathf.PI * 2f;
                float y = 0f;
                for (int k = 0; k < phases.Length; k++)
                {
                    y += hillAmplitude * HarmonicWeights[k] * Mathf.Sin((k + 1) * theta + phases[k]);
                }

                float u = (((i - dropStart) % n) + n) % n / (float)n; // 0 at the drop, 1 a lap later
                y += u < dropFraction
                    ? dropHeight * Mathf.Lerp(1f, dropFraction, Mathf.SmoothStep(0f, 1f, u / dropFraction))
                    : dropHeight * u;

                heights[i] = y;
            }

            for (int i = 0; i < n; i++)
            {
                var p = points[i];
                p.y = heights[i];
                points[i] = p;
            }

            // The lowest point of the *smoothed* road sits on the plain and
            // everything else rises out of it. Normalising on the control
            // points is not enough: the Catmull-Rom spline overshoots between
            // them, so a steep valley would dip below ground level, and the
            // ground plane and terrain (clamped to GroundLevel) would then
            // sit above the road there.
            float min = float.MaxValue;
            foreach (var s in TrackMeshBuilder.SmoothLoop(points)) min = Mathf.Min(min, s.y);
            for (int i = 0; i < n; i++)
            {
                var p = points[i];
                p.y -= min - GeneratedTrack.GroundLevel;
                points[i] = p;
            }
        }
    }
}
