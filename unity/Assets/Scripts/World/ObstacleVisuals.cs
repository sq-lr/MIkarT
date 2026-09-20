using System.Collections.Generic;
using MarioKart.Rendering;
using MarioKart.UI;
using UnityEngine;

namespace MarioKart.World
{
    /// <summary>
    /// Readable sign models for the track pickups, built at runtime from
    /// built-in primitive meshes and small extruded prisms (ADR 0005 — no
    /// hand-authored models). Boost is a green ">>", Paralyze a stop sign,
    /// Spin a Subway-logo-style pair of opposing arrows. Every part is
    /// collider-free; the pickup's root SphereCollider is the only trigger.
    /// Sign faces sit in the local XY plane so they face karts coming along
    /// the track; TrackObstacle spins the whole thing about Y.
    /// </summary>
    public static class ObstacleVisuals
    {
        private static readonly Color StopRed = new Color(0.85f, 0.12f, 0.12f);
        private static readonly Color PostGrey = new Color(0.55f, 0.57f, 0.60f);
        private static readonly Color SubwayYellow = new Color(0.98f, 0.78f, 0.10f);
        private static readonly Color SubwayGreen = new Color(0.00f, 0.55f, 0.30f);
        private static readonly Color BoostGreen = new Color(0.20f, 0.80f, 0.35f);

        private const float SignDepth = 0.12f;
        private const string TextShaderName = "MarioKart/Text3D";

        public static void Build(Transform root, ObstacleKind kind, float scale)
        {
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root, worldPositionStays: false);
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = Vector3.one * scale;

            switch (kind)
            {
                case ObstacleKind.Boost:
                    BuildChevrons(visual);
                    break;
                case ObstacleKind.Paralyze:
                    BuildStopSign(visual);
                    break;
                default:
                    BuildSpinArrows(visual);
                    break;
            }
        }

        // ── Boost: green ">>" ─────────────────────────────────────────────

        private static void BuildChevrons(Transform parent)
        {
            var material = Material("Boost", BoostGreen);
            // Each chevron reaches ChevronBack left of its tip, so shift the
            // pair right by that much to centre the whole ">>".
            BuildChevron(parent, material, ChevronBack - 0.18f);
            BuildChevron(parent, material, ChevronBack + 0.18f);
        }

        private const float ChevronArm = 0.42f;
        private static readonly float ChevronBack = ChevronArm * 0.5f * Mathf.Cos(45f * Mathf.Deg2Rad);

        /// <summary>One ">" with its tip at x: two slabs meeting at the tip,
        /// each arm's centre half an arm length back along its own 45° line.</summary>
        private static void BuildChevron(Transform parent, Material material, float x)
        {
            const float arm = ChevronArm, thick = 0.13f;
            float back = ChevronBack;
            Part("ChevronUpper", Cube, parent, material,
                new Vector3(x - back, back, 0f), new Vector3(0f, 0f, -45f), new Vector3(arm, thick, SignDepth));
            Part("ChevronLower", Cube, parent, material,
                new Vector3(x - back, -back, 0f), new Vector3(0f, 0f, 45f), new Vector3(arm, thick, SignDepth));
        }

        // ── Paralyze: stop sign ───────────────────────────────────────────

        private static void BuildStopSign(Transform parent)
        {
            const float borderRadius = 0.46f, faceRadius = 0.40f;
            const float borderDepth = 0.06f, faceDepth = 0.02f;
            float faceZ = borderDepth * 0.5f + faceDepth * 0.5f - 0.005f;

            var white = Material("SignWhite", Color.white);
            var red = Material("StopRed", StopRed);

            Prism("Border", Octagon(borderRadius), borderDepth, parent, white, Vector3.zero, Vector3.zero);
            Prism("FaceFront", Octagon(faceRadius), faceDepth, parent, red, new Vector3(0f, 0f, faceZ), Vector3.zero);
            Prism("FaceBack", Octagon(faceRadius), faceDepth, parent, red, new Vector3(0f, 0f, -faceZ), Vector3.zero);

            // Pickups hover 0.7 m over the road and bob ±0.12, so a post
            // reaching down to -0.55 stays clear of the asphalt.
            float postTop = -borderRadius * 0.9f, postBottom = -0.55f;
            Part("Post", Cylinder, parent, Material("Post", PostGrey),
                new Vector3(0f, (postTop + postBottom) * 0.5f, 0f), Vector3.zero,
                new Vector3(0.06f, (postTop - postBottom) * 0.5f, 0.06f));

            // TextMesh reads correctly from the -Z side, so the unrotated
            // copy goes on the back face and the 180° one on the front.
            float textZ = faceZ + faceDepth * 0.5f + 0.005f;
            Lettering("StopTextBack", "STOP", parent, new Vector3(0f, 0f, -textZ), 0f);
            Lettering("StopTextFront", "STOP", parent, new Vector3(0f, 0f, textZ), 180f);
        }

        // ── Spin: Subway-style opposing arrows ────────────────────────────

        private static void BuildSpinArrows(Transform parent)
        {
            BuildArrow(parent, Material("SubwayYellow", SubwayYellow), 0f);
            BuildArrow(parent, Material("SubwayGreen", SubwayGreen), 180f);
        }

        /// <summary>
        /// One L-shaped arrow: a horizontal shaft running right, turning up
        /// into a triangular head. Rotated 180° about Z it becomes the
        /// mirror-image arrow running left and pointing down.
        /// </summary>
        private static void BuildArrow(Transform parent, Material material, float rollDegrees)
        {
            const float thick = 0.12f;
            const float shaftLength = 0.55f, shaftY = 0.11f;
            const float elbowX = 0.14f, headBaseY = 0.28f, headHalfWidth = 0.15f, headHeight = 0.14f;

            var arrow = new GameObject("Arrow").transform;
            arrow.SetParent(parent, false);
            arrow.localPosition = Vector3.zero;
            arrow.localRotation = Quaternion.Euler(0f, 0f, rollDegrees);
            arrow.localScale = Vector3.one;

            Part("Shaft", Cube, arrow, material,
                new Vector3(elbowX - shaftLength * 0.5f + thick * 0.5f, shaftY, 0f), Vector3.zero,
                new Vector3(shaftLength, thick, SignDepth));
            Part("Riser", Cube, arrow, material,
                new Vector3(elbowX, (shaftY - thick * 0.5f + headBaseY) * 0.5f, 0f), Vector3.zero,
                new Vector3(thick, headBaseY - (shaftY - thick * 0.5f), SignDepth));
            Prism("Head", new[]
                {
                    new Vector2(-headHalfWidth, 0f),
                    new Vector2(headHalfWidth, 0f),
                    new Vector2(0f, headHeight),
                },
                SignDepth, arrow, material, new Vector3(elbowX, headBaseY, 0f), Vector3.zero);
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static Vector2[] Octagon(float radius)
        {
            var points = new Vector2[8];
            for (int i = 0; i < 8; i++)
            {
                // Start half a step in so a flat edge sits on the bottom.
                float angle = (i + 0.5f) * Mathf.PI * 2f / 8f;
                points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }
            return points;
        }

        private static MeshRenderer Part(string name, Mesh mesh, Transform parent, Material material,
            Vector3 localPosition, Vector3 localEuler, Vector3 localScale)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            go.transform.localScale = localScale;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        /// <summary>
        /// A closed prism extruded along Z from a convex outline in the XY
        /// plane (counter-clockwise with X right, Y up), centred on z = 0.
        /// Winding follows Unity's rule: cross(b - a, c - a) is the face normal.
        /// </summary>
        private static MeshRenderer Prism(string name, Vector2[] outline, float depth, Transform parent, Material material,
            Vector3 localPosition, Vector3 localEuler)
        {
            return Part(name, PrismMesh(name, outline, depth), parent, material, localPosition, localEuler, Vector3.one);
        }

        private static Mesh PrismMesh(string name, Vector2[] outline, float depth)
        {
            int n = outline.Length;
            float half = depth * 0.5f;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            // Front cap (+Z) and back cap (-Z), fan-triangulated; separate
            // vertices per face so RecalculateNormals keeps edges hard.
            int front = vertices.Count;
            for (int i = 0; i < n; i++) vertices.Add(new Vector3(outline[i].x, outline[i].y, half));
            for (int i = 1; i < n - 1; i++) triangles.AddRange(new[] { front, front + i, front + i + 1 });

            int back = vertices.Count;
            for (int i = 0; i < n; i++) vertices.Add(new Vector3(outline[i].x, outline[i].y, -half));
            for (int i = 1; i < n - 1; i++) triangles.AddRange(new[] { back, back + i + 1, back + i });

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int s = vertices.Count;
                vertices.Add(new Vector3(outline[i].x, outline[i].y, half));
                vertices.Add(new Vector3(outline[j].x, outline[j].y, half));
                vertices.Add(new Vector3(outline[j].x, outline[j].y, -half));
                vertices.Add(new Vector3(outline[i].x, outline[i].y, -half));
                triangles.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void Lettering(string name, string text, Transform parent, Vector3 localPosition, float yawDegrees)
        {
            var go = new GameObject(name, typeof(MeshRenderer), typeof(TextMesh));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);

            var font = GameFonts.Display;
            var textMesh = go.GetComponent<TextMesh>();
            textMesh.font = font;
            textMesh.text = text;
            textMesh.fontSize = 64;
            textMesh.fontStyle = FontStyle.Normal;
            textMesh.characterSize = 0.04f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = Color.white;
            go.GetComponent<MeshRenderer>().sharedMaterial = TextMaterial(font);
        }

        private static Material textMaterial;
        private static Font textFont;

        /// <summary>
        /// Depth-tested copy of the font material (the font's own material
        /// draws on top of everything). Tracks the dynamic font atlas so the
        /// letters don't vanish when the UI forces a texture rebuild.
        /// </summary>
        private static Material TextMaterial(Font font)
        {
            if (textMaterial != null && textFont == font) return textMaterial;

            var fontMaterial = font.material;
            var shader = Shader.Find(TextShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"ObstacleVisuals: shader '{TextShaderName}' not found; sign text will draw on top of the world");
                shader = fontMaterial.shader;
            }
            textMaterial = new Material(shader) { name = "SignText", mainTexture = fontMaterial.mainTexture };
            textFont = font;
            Font.textureRebuilt -= OnFontTextureRebuilt;
            Font.textureRebuilt += OnFontTextureRebuilt;
            return textMaterial;
        }

        private static void OnFontTextureRebuilt(Font font)
        {
            if (font == textFont && textMaterial != null && font.material != null)
            {
                textMaterial.mainTexture = font.material.mainTexture;
            }
        }

        private static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

        private static Material Material(string name, Color color)
        {
            if (!materials.TryGetValue(name, out var material) || material == null)
            {
                material = ToonStyle.Create(color, outline: true, name: name);
                materials[name] = material;
            }
            return material;
        }

        // Built-in primitive meshes, without the colliders CreatePrimitive
        // would attach.
        private static Mesh cube, cylinder;
        private static Mesh Cube => cube != null ? cube : cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        private static Mesh Cylinder => cylinder != null ? cylinder : cylinder = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
    }
}
