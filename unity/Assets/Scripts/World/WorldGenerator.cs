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

        /// <summary>The generated-mesh loader, so GameManager can wait on it.</summary>
        public MarioKart.AssetsSystem.GeneratedMeshLoader MeshLoader =>
            environmentGenerator != null ? environmentGenerator.MeshLoader : null;

        public void Generate(WorldRecipe recipe)
        {
            int trackSeed = WorldRandom.DeriveSeed(recipe.seed, "track");
            int envSeed = WorldRandom.DeriveSeed(recipe.seed, "environment");

            var track = new TrackGenerator().Generate(recipe, trackSeed);
            CurrentTrack = track;

            BuildTrackVisual(track, recipe.palette, recipe.track != null ? recipe.track.surface : "concrete");
            BuildCheckpoints(track);

            if (environmentGenerator != null)
            {
                environmentGenerator.Generate(recipe, track, envSeed);
            }

            ApplyPalette(recipe.palette);
            ApplySky(recipe.world != null ? recipe.world.sky : "sunny");

            OnWorldReady?.Invoke();
        }

        /// <summary>
        /// Road ribbon + barrier walls (see TrackMeshBuilder). Walls are
        /// tinted from palette[2] when present so they pick up the theme.
        /// </summary>
        private void BuildTrackVisual(GeneratedTrack track, List<string> palette, string surface)
        {
            if (trackVisualRoot == null) return;

            var roadColor = TrackMeshBuilder.SurfaceColor(surface);
            var wallColor = new Color(0.9f, 0.9f, 0.9f);
            if (palette != null && palette.Count > 2 && ColorUtility.TryParseHtmlString(palette[2], out var tint))
            {
                wallColor = Color.Lerp(wallColor, tint, 0.5f);
            }

            TrackMeshBuilder.Build(trackVisualRoot, track, roadColor, wallColor);
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

        /// <summary>
        /// Sky preset chosen on the upload screen (recipe.world.sky): tints
        /// ambient light and the cameras' clear colour.
        /// </summary>
        private void ApplySky(string sky)
        {
            Color background;
            Color ambient;
            switch (sky)
            {
                case "cloudy":
                    background = new Color(0.42f, 0.48f, 0.56f);
                    ambient = new Color(0.55f, 0.60f, 0.68f);
                    break;
                case "sunset":
                    background = new Color(0.82f, 0.30f, 0.16f);
                    ambient = new Color(0.72f, 0.38f, 0.25f);
                    break;
                case "night":
                    background = new Color(0.03f, 0.05f, 0.14f);
                    ambient = new Color(0.10f, 0.13f, 0.24f);
                    break;
                default:
                    background = new Color(0.35f, 0.70f, 0.95f);
                    ambient = new Color(0.70f, 0.75f, 0.82f);
                    break;
            }

            RenderSettings.ambientLight = ambient;
            foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = background;
            }
        }
    }
}
