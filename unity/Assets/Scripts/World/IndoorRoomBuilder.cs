using System.Collections.Generic;
using MarioKart.Rendering;
using UnityEngine;

namespace MarioKart.World
{
    /// <summary>
    /// The "indoor" sky preset: instead of a sky, the track sits inside a
    /// big toy-box room -- four walls with a skirting board, picture rail
    /// and a few windows, under a ceiling of glowing light panels. Sized to
    /// the generated loop so any track fits, and built from the built-in
    /// cube mesh with runtime toon materials (ADR 0005 -- nothing is a
    /// hand-authored asset). Purely visual: the track barriers keep the
    /// karts in, so no part carries a collider.
    /// </summary>
    public static class IndoorRoomBuilder
    {
        public const string RootName = "IndoorRoom";

        private const float Margin = 90f;        // metres of floor past the loop's widest point
        private const float MinHalfExtent = 140f;
        private const float WallHeight = 50f;
        private const float WallThickness = 2f;
        private const float SkirtingHeight = 2.2f;
        private const float RailHeight = 0.8f;
        private const float RailY = WallHeight * 0.62f;
        private const int PanelsPerSide = 4;
        private const float PanelSize = 14f;

        public static readonly Color Cream = new Color(0.93f, 0.85f, 0.68f);
        public static readonly Color Ceiling = new Color(0.96f, 0.94f, 0.90f);
        public static readonly Color Wood = new Color(0.50f, 0.34f, 0.22f);
        public static readonly Color Floor = new Color(0.72f, 0.55f, 0.38f);
        private static readonly Color PanelGlow = new Color(1f, 0.98f, 0.90f);
        private static readonly Color WindowPane = new Color(0.62f, 0.82f, 0.94f);

        /// <summary>Wall colour for a recipe: its accent pulled well toward cream so the room stays light.</summary>
        public static Color WallColor(List<string> palette)
        {
            Color accent = Cream;
            if (palette != null && palette.Count > 2) ColorUtility.TryParseHtmlString(palette[2], out accent);
            return Color.Lerp(accent, Cream, 0.65f);
        }

        public static void Remove(Transform parent)
        {
            var existing = parent.Find(RootName);
            if (existing != null) Object.Destroy(existing.gameObject);
        }

        public static GameObject Build(Transform parent, GeneratedTrack track, List<string> palette)
        {
            Remove(parent);
            var root = new GameObject(RootName);
            root.transform.SetParent(parent, worldPositionStays: false);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // Room footprint: the loop's bounding box plus a margin, never
            // smaller than a sensible hall so a tiny track doesn't get a
            // cupboard.
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
            var samples = track != null ? track.Samples : null;
            if (samples != null && samples.Count > 0)
            {
                foreach (var p in samples)
                {
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                    minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                }
            }
            else
            {
                minX = minZ = -MinHalfExtent; maxX = maxZ = MinHalfExtent;
            }
            float reach = track != null ? track.MaxApronReach() : 0f;
            Vector3 centre = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);
            float halfX = Mathf.Max(MinHalfExtent, (maxX - minX) * 0.5f + reach + Margin);
            float halfZ = Mathf.Max(MinHalfExtent, (maxZ - minZ) * 0.5f + reach + Margin);

            var wall = ToonStyle.Create(WallColor(palette), outline: false, name: "RoomWall");
            var trim = ToonStyle.Create(Wood, outline: true, name: "RoomTrim");
            var ceiling = ToonStyle.Create(Ceiling, outline: false, name: "RoomCeiling");
            var panel = ToonStyle.Create(PanelGlow, outline: true, name: "RoomLightPanel");
            var pane = ToonStyle.Create(WindowPane, outline: true, name: "RoomWindow");
            var frame = ToonStyle.Create(Color.white, outline: true, name: "RoomWindowFrame");

            // Four walls, each a box just outside the footprint. Yaw 0 faces
            // -Z: the wall's local X runs along its length.
            BuildWall(root.transform, "Wall_North", centre + new Vector3(0f, 0f, halfZ), 0f, halfX * 2f, wall, trim, pane, frame, windows: true);
            BuildWall(root.transform, "Wall_South", centre + new Vector3(0f, 0f, -halfZ), 180f, halfX * 2f, wall, trim, pane, frame, windows: true);
            BuildWall(root.transform, "Wall_East", centre + new Vector3(halfX, 0f, 0f), 90f, halfZ * 2f, wall, trim, pane, frame, windows: false);
            BuildWall(root.transform, "Wall_West", centre + new Vector3(-halfX, 0f, 0f), -90f, halfZ * 2f, wall, trim, pane, frame, windows: false);

            Box("Ceiling", root.transform, ceiling,
                centre + new Vector3(0f, WallHeight + WallThickness * 0.5f, 0f),
                Vector3.zero, new Vector3(halfX * 2f + WallThickness * 2f, WallThickness, halfZ * 2f + WallThickness * 2f));

            // A grid of light panels hanging just under the ceiling.
            for (int ix = 0; ix < PanelsPerSide; ix++)
            {
                for (int iz = 0; iz < PanelsPerSide; iz++)
                {
                    float fx = (ix + 0.5f) / PanelsPerSide * 2f - 1f;
                    float fz = (iz + 0.5f) / PanelsPerSide * 2f - 1f;
                    Box($"LightPanel_{ix}_{iz}", root.transform, panel,
                        centre + new Vector3(fx * halfX * 0.8f, WallHeight - 0.4f, fz * halfZ * 0.8f),
                        Vector3.zero, new Vector3(PanelSize, 0.6f, PanelSize));
                }
            }

            return root;
        }

        private static void BuildWall(Transform parent, string name, Vector3 centre, float yaw, float length,
            Material wall, Material trim, Material pane, Material frame, bool windows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.SetPositionAndRotation(centre, Quaternion.Euler(0f, yaw, 0f));

            // Wall slab, sitting on the floor and pushed half its thickness outward.
            Box("Slab", go.transform, wall,
                new Vector3(0f, WallHeight * 0.5f, WallThickness * 0.5f), Vector3.zero,
                new Vector3(length + WallThickness * 2f, WallHeight, WallThickness));
            // Skirting board and picture rail stand proud of the slab.
            Box("Skirting", go.transform, trim,
                new Vector3(0f, SkirtingHeight * 0.5f, -0.25f), Vector3.zero,
                new Vector3(length, SkirtingHeight, 0.5f));
            Box("Rail", go.transform, trim,
                new Vector3(0f, RailY, -0.2f), Vector3.zero,
                new Vector3(length, RailHeight, 0.4f));

            if (!windows) return;

            // Three tall windows: a white frame with a sky-blue pane inset.
            float windowWidth = Mathf.Min(24f, length * 0.14f);
            float windowHeight = WallHeight * 0.36f;
            float windowY = RailY + windowHeight * 0.5f + 2f;
            for (int i = -1; i <= 1; i++)
            {
                float x = i * length * 0.28f;
                Box($"WindowFrame_{i + 1}", go.transform, frame,
                    new Vector3(x, windowY, -0.15f), Vector3.zero,
                    new Vector3(windowWidth + 1.6f, windowHeight + 1.6f, 0.3f));
                Box($"WindowPane_{i + 1}", go.transform, pane,
                    new Vector3(x, windowY, -0.35f), Vector3.zero,
                    new Vector3(windowWidth, windowHeight, 0.3f));
                // Cross bars so it reads as a window, not a poster.
                Box($"WindowBarV_{i + 1}", go.transform, frame,
                    new Vector3(x, windowY, -0.45f), Vector3.zero, new Vector3(0.7f, windowHeight, 0.2f));
                Box($"WindowBarH_{i + 1}", go.transform, frame,
                    new Vector3(x, windowY, -0.45f), Vector3.zero, new Vector3(windowWidth, 0.7f, 0.2f));
            }
        }

        private static void Box(string name, Transform parent, Material material, Vector3 localPosition, Vector3 localEuler, Vector3 size)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            go.transform.localScale = size;
            go.GetComponent<MeshFilter>().sharedMesh = Cube;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // receive only, like the ground
            go.isStatic = true;
        }

        // Built-in cube mesh, without the collider CreatePrimitive would attach.
        private static Mesh cube;
        private static Mesh Cube => cube != null ? cube : cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
    }
}
