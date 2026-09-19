using System.Collections.Generic;
using MarioKart.AI;
using UnityEngine;

namespace MarioKart.World
{
    /// <summary>
    /// Output of TrackGenerator.Generate. Not given its own file in the
    /// original listing -- kept alongside its producer.
    /// </summary>
    public class GeneratedTrack
    {
        public List<Vector3> controlPoints;
        public List<Vector3> checkpointPositions;
        public float width;
    }

    /// <summary>
    /// Builds ONE simple loop track from a WorldRecipe. Deterministic: the
    /// same recipe + seed always produces the same control points and
    /// checkpoints. No multi-template, branching, or jump support -- that is
    /// explicitly out of scope.
    /// </summary>
    public class TrackGenerator
    {
        private const int ControlPointCount = 24;
        private const int CheckpointStride = 3; // 24 / 8 checkpoints

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

                if (i % CheckpointStride == 0)
                {
                    checkpoints.Add(point);
                }
            }

            return new GeneratedTrack
            {
                controlPoints = controlPoints,
                checkpointPositions = checkpoints,
                width = recipe.track.width,
            };
        }
    }
}
