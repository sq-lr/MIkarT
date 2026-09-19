using System;
using System.Collections.Generic;
using MarioKart.AI;
using MarioKart.Racing;
using UnityEngine;

namespace MarioKart.World
{
    /// <summary>
    /// Orchestrates world generation: derives sub-seeds from
    /// recipe.seed, builds the track, spawns checkpoints, populates the
    /// environment, and tints placeholder materials from the palette. This
    /// is the boundary where the WorldRecipe (data) becomes an actual scene
    /// (Unity objects).
    /// </summary>
    public class WorldGenerator : MonoBehaviour
    {
        [SerializeField] private EnvironmentGenerator environmentGenerator;
        [SerializeField] private Transform trackVisualRoot;
        [SerializeField] private Transform checkpointRoot;
        [SerializeField] private Light sunLight;
        [SerializeField] private Renderer groundRenderer;

        public event Action OnWorldReady;

        public GeneratedTrack CurrentTrack { get; private set; }

        public void Generate(WorldRecipe recipe)
        {
            int trackSeed = WorldRandom.DeriveSeed(recipe.seed, "track");
            int envSeed = WorldRandom.DeriveSeed(recipe.seed, "environment");

            var track = new TrackGenerator().Generate(recipe, trackSeed);
            CurrentTrack = track;

            BuildTrackVisual(track);
            BuildCheckpoints(track);

            if (environmentGenerator != null)
            {
                environmentGenerator.Generate(recipe, track, envSeed);
            }

            ApplyPalette(recipe.palette);

            OnWorldReady?.Invoke();
        }

        private void BuildTrackVisual(GeneratedTrack track)
        {
            if (trackVisualRoot == null) return;

            var line = trackVisualRoot.GetComponent<LineRenderer>();
            if (line == null) line = trackVisualRoot.gameObject.AddComponent<LineRenderer>();

            var points = new List<Vector3>(track.controlPoints) { track.controlPoints[0] };
            line.positionCount = points.Count;
            line.SetPositions(points.ToArray());
            line.loop = true;
            line.widthMultiplier = track.width;
        }

        private void BuildCheckpoints(GeneratedTrack track)
        {
            if (checkpointRoot == null) return;

            foreach (Transform child in checkpointRoot)
            {
                Destroy(child.gameObject);
            }

            for (int i = 0; i < track.checkpointPositions.Count; i++)
            {
                var go = new GameObject($"Checkpoint_{i}");
                go.transform.SetParent(checkpointRoot, worldPositionStays: false);
                go.transform.position = track.checkpointPositions[i];
                // Face the track tangent so the (width, 4, 2) box below is a
                // gate *across* the track everywhere on the loop, not just
                // where the track happens to run along the Z axis.
                go.transform.rotation = Quaternion.LookRotation(TangentAt(track, i * (track.controlPoints.Count / track.checkpointPositions.Count)), Vector3.up);

                var collider = go.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = new Vector3(track.width, 4f, 2f);

                var checkpoint = go.AddComponent<Checkpoint>();
                checkpoint.checkpointIndex = i;
            }
        }

        /// <summary>
        /// Direction of travel at control point `index` (central difference
        /// around the loop). Shared by checkpoints and the start-line
        /// placement in RaceBootstrap.
        /// </summary>
        public static Vector3 TangentAt(GeneratedTrack track, int index)
        {
            int count = track.controlPoints.Count;
            var prev = track.controlPoints[(index - 1 + count) % count];
            var next = track.controlPoints[(index + 1) % count];
            var tangent = next - prev;
            tangent.y = 0f;
            return tangent.sqrMagnitude > 0f ? tangent.normalized : Vector3.forward;
        }

        private void ApplyPalette(List<string> palette)
        {
            if (palette == null || palette.Count == 0) return;

            if (sunLight != null && ColorUtility.TryParseHtmlString(palette[0], out var lightColor))
            {
                sunLight.color = lightColor;
            }

            if (groundRenderer != null && palette.Count > 1 && ColorUtility.TryParseHtmlString(palette[1], out var groundColor))
            {
                groundRenderer.material.color = groundColor;
            }
        }
    }
}
