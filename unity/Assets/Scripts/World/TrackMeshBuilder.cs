using System.Collections.Generic;
using UnityEngine;

namespace MarioKart.World
{
    /// <summary>
    /// Turns a GeneratedTrack (a coarse loop of control points) into the
    /// physical track: a smooth road ribbon plus a solid barrier wall along
    /// each edge. Purely geometric and deterministic -- no randomness, so it
    /// needs no seed. The barrier walls carry a TrackBarrier so karts that
    /// touch them are slowed (see KartController).
    /// </summary>
    public static class TrackMeshBuilder
    {
        // Catmull-Rom subdivisions per control-point segment. 24 control
        // points × 8 = 192 samples around the loop.
        private const int SubdivisionsPerSegment = 8;

        private const float RoadHeight = 0.02f;   // just above the ground plane
        private const float WallHeight = 1.2f;
        private const float WallThickness = 0.5f;

        // Finish line: checkered strip on the road at checkpoint 0, with a
        // post either side and a banner across the top.
        private const float FinishStripDepth = 3f;
        private const float FinishCheckerSize = 1f;    // metres per square
        private const float FinishPostHeight = 6f;
        private const float FinishPostRadius = 0.3f;
        private const float FinishBannerHeight = 1f;

        public static void Build(Transform root, GeneratedTrack track, Color roadColor, Color wallColor, Color curbColor)
        {
            foreach (Transform child in root)
            {
                Object.Destroy(child.gameObject);
            }

            List<Vector3> centers = SmoothLoop(track.controlPoints);
            int n = centers.Count;
            var rights = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 tangent = centers[(i + 1) % n] - centers[(i - 1 + n) % n];
                tangent.y = 0f;
                rights[i] = Vector3.Cross(Vector3.up, tangent.normalized);
            }

            float halfWidth = track.width * 0.5f;

            BuildRoad(root, centers, rights, halfWidth, roadColor);
            BuildLanePaint(root, centers, rights, halfWidth);
            BuildCurbs(root, centers, rights, halfWidth, curbColor);
            BuildWall(root, "Barrier_Left", centers, rights, -halfWidth, -1f, wallColor);
            BuildWall(root, "Barrier_Right", centers, rights, halfWidth, 1f, wallColor);

            // The smoothed loop passes through every control point, and
            // sample 0 is control point 0 == checkpoint 0 == start/finish.
            Vector3 finishForward = Vector3.Cross(rights[0], Vector3.up);
            BuildFinishLine(root, centers[0], finishForward, rights[0], halfWidth);
        }

        public static Color SurfaceColor(string surface)
        {
            switch (surface)
            {
                // Forest-reference palette: terracotta, slate, moss and
                // deep earth sit against the image's dark teal canopy.
                case "red_bricks": return new Color(0.57f, 0.20f, 0.14f);
                case "grey_tiles": return new Color(0.28f, 0.38f, 0.38f);
                case "stone_slabs": return new Color(0.32f, 0.42f, 0.34f);
                case "dirt": return new Color(0.29f, 0.18f, 0.11f);
                default: return new Color(0.19f, 0.29f, 0.28f);
            }
        }

        // ------------------------------------------------------------------

        private static void BuildFinishLine(Transform root, Vector3 center, Vector3 forward, Vector3 right, float halfWidth)
        {
            var finish = new GameObject("FinishLine");
            finish.transform.SetPositionAndRotation(center, Quaternion.LookRotation(forward, Vector3.up));
            finish.transform.SetParent(root, worldPositionStays: true);

            var checker = CheckerMaterial();

            // Checkered strip across the road. UVs are in metres / checker
            // size so the squares stay 1 m regardless of track width.
            var builder = new MeshBuilder();
            float halfDepth = FinishStripDepth * 0.5f;
            Vector3 up = Vector3.up * (RoadHeight + 0.03f); // above the road, no z-fighting
            Vector3 a = center - right * halfWidth - forward * halfDepth + up;
            Vector3 b = center - right * halfWidth + forward * halfDepth + up;
            Vector3 c = center + right * halfWidth + forward * halfDepth + up;
            Vector3 d = center + right * halfWidth - forward * halfDepth + up;
            float uMax = (halfWidth * 2f) / FinishCheckerSize / 2f;
            float vMax = FinishStripDepth / FinishCheckerSize / 2f;
            builder.AddQuad(a, b, c, d, Vector3.up,
                new Vector2(0f, 0f), new Vector2(0f, vMax), new Vector2(uMax, vMax), new Vector2(uMax, 0f));

            var strip = new GameObject("Strip");
            strip.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            strip.transform.SetParent(finish.transform, worldPositionStays: true);
            Mesh stripMesh = builder.ToMesh("FinishStrip");
            strip.AddComponent<MeshFilter>().sharedMesh = stripMesh;
            strip.AddComponent<MeshRenderer>().sharedMaterial = checker;

            // Posts just inside each wall, banner across the top.
            var postMaterial = MarioKart.Rendering.GhibliLook.Lit(MarioKart.Rendering.GhibliLook.FenceWood);
            float postInset = WallThickness + FinishPostRadius;
            for (int side = -1; side <= 1; side += 2)
            {
                var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                post.name = side < 0 ? "Post_Left" : "Post_Right";
                Object.Destroy(post.GetComponent<Collider>()); // decoration only
                post.transform.SetParent(finish.transform, worldPositionStays: false);
                post.transform.localPosition = new Vector3(side * (halfWidth - postInset), FinishPostHeight * 0.5f, 0f);
                post.transform.localScale = new Vector3(FinishPostRadius * 2f, FinishPostHeight * 0.5f, FinishPostRadius * 2f);
                post.GetComponent<Renderer>().sharedMaterial = postMaterial;
            }

            var banner = GameObject.CreatePrimitive(PrimitiveType.Cube);
            banner.name = "Banner";
            Object.Destroy(banner.GetComponent<Collider>());
            banner.transform.SetParent(finish.transform, worldPositionStays: false);
            banner.transform.localPosition = new Vector3(0f, FinishPostHeight - FinishBannerHeight * 0.5f, 0f);
            banner.transform.localScale = new Vector3(halfWidth * 2f - postInset * 2f, FinishBannerHeight, 0.2f);
            var bannerRenderer = banner.GetComponent<Renderer>();
            bannerRenderer.sharedMaterial = checker;
            // Cube UVs run 0..1 per face; tile so the squares match the strip.
            bannerRenderer.material.mainTextureScale = new Vector2(halfWidth * 2f / FinishCheckerSize / 2f, FinishBannerHeight / FinishCheckerSize / 2f);
        }

        /// <summary>2×2 cream/moss checker, point-filtered and repeating.</summary>
        private static Material CheckerMaterial()
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                name = "Checker",
            };
            var light = MarioKart.Rendering.GhibliLook.Cream;
            var dark = new Color(0.38f, 0.42f, 0.34f);
            tex.SetPixels(new[] { light, dark, dark, light });
            tex.Apply();

            var mat = MarioKart.Rendering.GhibliLook.Lit(Color.white);
            mat.name = "Checker";
            mat.mainTexture = tex;
            return mat;
        }

        // ------------------------------------------------------------------

        private static void BuildRoad(Transform root, List<Vector3> centers, Vector3[] rights, float halfWidth, Color color)
        {
            int n = centers.Count;
            var builder = new MeshBuilder();

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector3 up = Vector3.up * RoadHeight;
                Vector3 li = centers[i] - rights[i] * halfWidth + up;
                Vector3 ri = centers[i] + rights[i] * halfWidth + up;
                Vector3 lj = centers[j] - rights[j] * halfWidth + up;
                Vector3 rj = centers[j] + rights[j] * halfWidth + up;
                builder.AddQuad(li, ri, rj, lj, Vector3.up);
            }

            var go = CreateMeshObject(root, "Road", builder, color, withCollider: false);
            go.isStatic = true;
        }

        /// <summary>
        /// Dashed center line + solid edge lines. Separate meshes so they
        /// sit slightly above the asphalt without fighting its UVs.
        /// </summary>
        private static void BuildLanePaint(Transform root, List<Vector3> centers, Vector3[] rights, float halfWidth)
        {
            const float paintHeight = RoadHeight + 0.03f;
            const float centerHalf = 0.16f;
            const float edgeHalf = 0.14f;
            const float edgeInset = 0.7f;
            const int dashOn = 4;
            const int dashOff = 4;
            var paint = new Color(0.96f, 0.96f, 0.92f);

            int n = centers.Count;
            var dashes = new MeshBuilder();
            var edges = new MeshBuilder();
            Vector3 up = Vector3.up * paintHeight;
            int cycle = dashOn + dashOff;

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                if ((i % cycle) < dashOn)
                {
                    Vector3 li = centers[i] - rights[i] * centerHalf + up;
                    Vector3 ri = centers[i] + rights[i] * centerHalf + up;
                    Vector3 lj = centers[j] - rights[j] * centerHalf + up;
                    Vector3 rj = centers[j] + rights[j] * centerHalf + up;
                    dashes.AddQuad(li, ri, rj, lj, Vector3.up);
                }

                float inset = halfWidth - edgeInset;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 ai = centers[i] + rights[i] * (side * (inset - edgeHalf)) + up;
                    Vector3 bi = centers[i] + rights[i] * (side * (inset + edgeHalf)) + up;
                    Vector3 aj = centers[j] + rights[j] * (side * (inset - edgeHalf)) + up;
                    Vector3 bj = centers[j] + rights[j] * (side * (inset + edgeHalf)) + up;
                    edges.AddQuad(ai, bi, bj, aj, Vector3.up);
                }
            }

            CreateMeshObject(root, "CenterLine", dashes, paint, withCollider: false).isStatic = true;
            CreateMeshObject(root, "EdgeLines", edges, paint, withCollider: false).isStatic = true;
        }

        /// <summary>Red/white (or palette) kerbs along each wall, Mario Kart style.</summary>
        private static void BuildCurbs(Transform root, List<Vector3> centers, Vector3[] rights, float halfWidth, Color accent)
        {
            const float curbWidth = 0.55f;
            const float curbHeight = 0.07f;
            const int stripeSamples = 5;
            var white = Color.white;

            int n = centers.Count;
            var a = new MeshBuilder();
            var b = new MeshBuilder();

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                bool stripeA = (i / stripeSamples) % 2 == 0;
                var dest = stripeA ? a : b;
                Vector3 top = Vector3.up * curbHeight;

                for (int side = -1; side <= 1; side += 2)
                {
                    float inner = halfWidth - curbWidth;
                    float outer = halfWidth;
                    Vector3 i0 = centers[i] + rights[i] * (side * inner);
                    Vector3 i1 = centers[i] + rights[i] * (side * outer);
                    Vector3 j0 = centers[j] + rights[j] * (side * inner);
                    Vector3 j1 = centers[j] + rights[j] * (side * outer);
                    dest.AddQuad(i0 + top, i1 + top, j1 + top, j0 + top, Vector3.up);
                }
            }

            CreateMeshObject(root, "Curb_A", a, white, withCollider: false).isStatic = true;
            CreateMeshObject(root, "Curb_B", b, accent, withCollider: false).isStatic = true;
        }

        /// <summary>
        /// A closed, thick strip: inner face (toward the road), outer face,
        /// and top. Front faces point outward so the kart always hits a
        /// front face from the road side (mesh colliders are one-sided).
        /// </summary>
        private static void BuildWall(Transform root, string name, List<Vector3> centers, Vector3[] rights,
            float edgeOffset, float outwardSign, Color color)
        {
            int n = centers.Count;
            var builder = new MeshBuilder();
            Vector3 top = Vector3.up * WallHeight;

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector3 outwardI = rights[i] * outwardSign;
                Vector3 outwardJ = rights[j] * outwardSign;

                Vector3 innerI = centers[i] + rights[i] * edgeOffset;
                Vector3 innerJ = centers[j] + rights[j] * edgeOffset;
                Vector3 outerI = innerI + outwardI * WallThickness;
                Vector3 outerJ = innerJ + outwardJ * WallThickness;

                builder.AddQuad(innerI, innerI + top, innerJ + top, innerJ, -outwardI);     // faces the road
                builder.AddQuad(outerI, outerI + top, outerJ + top, outerJ, outwardI);      // faces away
                builder.AddQuad(innerI + top, outerI + top, outerJ + top, innerJ + top, Vector3.up);
            }

            var go = CreateMeshObject(root, name, builder, color, withCollider: true);
            go.isStatic = true;
            go.AddComponent<TrackBarrier>();
        }

        private static GameObject CreateMeshObject(Transform root, string name, MeshBuilder builder, Color color, bool withCollider)
        {
            // Vertices are in world space, so the object must sit at world
            // identity no matter how the root happens to be transformed.
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.SetParent(root, worldPositionStays: true);

            Mesh mesh = builder.ToMesh(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = MarioKart.Rendering.GhibliLook.Lit(color);

            if (withCollider)
            {
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
            return go;
        }

        /// <summary>
        /// Closed Catmull-Rom spline through the control points, so the road
        /// and walls curve instead of kinking at every control point.
        /// </summary>
        public static List<Vector3> SmoothLoop(List<Vector3> points)
        {
            int n = points.Count;
            var result = new List<Vector3>(n * SubdivisionsPerSegment);

            for (int i = 0; i < n; i++)
            {
                Vector3 p0 = points[(i - 1 + n) % n];
                Vector3 p1 = points[i];
                Vector3 p2 = points[(i + 1) % n];
                Vector3 p3 = points[(i + 2) % n];

                for (int s = 0; s < SubdivisionsPerSegment; s++)
                {
                    float t = s / (float)SubdivisionsPerSegment;
                    float t2 = t * t, t3 = t2 * t;
                    Vector3 p = 0.5f * (
                        2f * p1 +
                        (-p0 + p2) * t +
                        (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                        (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                    result.Add(p);
                }
            }
            return result;
        }

        /// <summary>
        /// Accumulates quads and fixes each one's winding so its front face
        /// points along the normal the caller asked for.
        /// </summary>
        private class MeshBuilder
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<int> triangles = new List<int>();
            private readonly List<Vector2> uvs = new List<Vector2>();

            public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 desiredNormal)
            {
                AddQuad(a, b, c, d, desiredNormal, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f));
            }

            public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 desiredNormal,
                Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD)
            {
                int start = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                uvs.Add(uvA); uvs.Add(uvB); uvs.Add(uvC); uvs.Add(uvD);

                bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), desiredNormal) < 0f;
                if (flip)
                {
                    triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
                }
                else
                {
                    triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                }
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                if (vertices.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0);
                mesh.SetUVs(0, uvs);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
