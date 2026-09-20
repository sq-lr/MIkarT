using System.Collections.Generic;
using MarioKart.Rendering;
using UnityEngine;

namespace MarioKart.World
{
    /// <summary>
    /// Turns a GeneratedTrack (a coarse loop of control points) into the
    /// physical track: a smooth road ribbon (with a collider -- the road is
    /// what the karts drive on now that it has hills), a solid barrier wall
    /// along each edge, and an embankment sloping from each wall down to
    /// the ground plane wherever the road is raised. Purely geometric and
    /// deterministic -- no randomness, so it needs no seed. The barrier
    /// walls carry a TrackBarrier so karts that touch them are slowed (see
    /// KartController).
    /// </summary>
    public static class TrackMeshBuilder
    {
        // Catmull-Rom subdivisions per control-point segment. 24 control
        // points × 8 = 192 samples around the loop.
        private const int SubdivisionsPerSegment = 8;

        public const float RoadHeight = 0.02f;    // road surface above the centreline
        public const float WallThickness = 0.5f;
        private const float WallHeight = 1.2f;
        // Terrain height-field (see BuildTerrain).
        private const float TerrainCellSize = 2f;         // metres, minimum
        private const int TerrainMaxCellsPerSide = 220;   // caps the vertex count for long loops
        private const float TerrainMargin = 6f;           // flat skirt past the widest embankment
        private const float UnderRoadDip = 0.4f;          // hidden beneath the road; avoids z-fighting it
        private const float TerrainUvMetres = 8f;         // metres per UV tile if the ground material is textured

        // Finish line: checkered strip on the road at checkpoint 0, with a
        // post either side and a banner across the top.
        private const float FinishStripDepth = 3f;
        private const float FinishCheckerSize = 1f;    // metres per square
        private const float FinishPostHeight = 6f;
        private const float FinishPostRadius = 0.3f;
        private const float FinishBannerHeight = 1f;

        /// <param name="curbColor">Accent stripe colour for the kerbs (alternates with white).</param>
        /// <param name="groundMaterial">The ground plane's material, shared by the terrain mesh so the embankments blend into the plain (any later palette tint applies to both).</param>
        public static void Build(Transform root, GeneratedTrack track, Color roadColor, Color wallColor, Color curbColor, Material groundMaterial)
        {
            foreach (Transform child in root)
            {
                Object.Destroy(child.gameObject);
            }

            List<Vector3> centers = track.Samples;
            int n = centers.Count;
            // Horizontal "right" vectors: the road tilts along its length
            // but stays flat across, so its edges are level with the centre.
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
            BuildTerrain(root, track, groundMaterial);

            // The smoothed loop passes through every control point, and
            // sample 0 is control point 0 == checkpoint 0 == start/finish.
            Vector3 finishForward = Vector3.Cross(rights[0], Vector3.up);
            BuildFinishLine(root, centers[0], finishForward, track.SampleTangent(0), rights[0], halfWidth);
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

        /// <param name="forward">Horizontal direction of travel (posts and banner stay upright).</param>
        /// <param name="slopeForward">Direction of travel along the road surface, so the strip lies on a hill instead of cutting through it.</param>
        private static void BuildFinishLine(Transform root, Vector3 center, Vector3 forward, Vector3 slopeForward, Vector3 right, float halfWidth)
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
            Vector3 a = center - right * halfWidth - slopeForward * halfDepth + up;
            Vector3 b = center - right * halfWidth + slopeForward * halfDepth + up;
            Vector3 c = center + right * halfWidth + slopeForward * halfDepth + up;
            Vector3 d = center + right * halfWidth - slopeForward * halfDepth + up;
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
            var postMaterial = ToonStyle.Create(Color.white, name: "FinishPost");
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

            return ToonStyle.Create(Color.white, texture: tex, name: "Checker");
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

            // No outline: a flat ribbon has no silhouette worth drawing.
            // The collider is what the karts drive on (the ground plane is
            // below the road wherever it climbs).
            var go = CreateMeshObject(root, "Road", builder, color, withCollider: true, outline: false);
            go.isStatic = true;
            // Receive only. A near-flat surface has little to cast onto, and
            // letting it cast makes it shadow *itself* (shadow acne) -- which
            // shows up as a huge dark blot around the camera that fades out
            // at the shadow distance.
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>
        /// The embankments, as one height-field mesh over the loop's
        /// footprint sampled from GeneratedTrack.GroundHeightAt -- the same
        /// function that rests environment objects on the ground, so what
        /// you see is exactly what they stand on. A regular grid cannot fold
        /// or leave holes the way strips extruded along the road do on the
        /// inside of tight bends. Beyond the embankments it lies at
        /// GroundLevel, a hair above the Ground plane and in the same
        /// material, so the two blend. Under the road it dips slightly so
        /// the road surface never z-fights it.
        /// </summary>
        private static void BuildTerrain(Transform root, GeneratedTrack track, Material groundMaterial)
        {
            var samples = track.Samples;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var p in samples)
            {
                min = Vector2.Min(min, new Vector2(p.x, p.z));
                max = Vector2.Max(max, new Vector2(p.x, p.z));
            }
            float margin = track.MaxApronReach() + TerrainMargin;
            min -= Vector2.one * margin;
            max += Vector2.one * margin;

            // Cell size grows with the footprint so a 2 km loop doesn't build
            // a million-vertex mesh.
            float extent = Mathf.Max(max.x - min.x, max.y - min.y);
            float cell = Mathf.Max(TerrainCellSize, extent / TerrainMaxCellsPerSide);
            int cols = Mathf.CeilToInt((max.x - min.x) / cell) + 1;
            int rows = Mathf.CeilToInt((max.y - min.y) / cell) + 1;

            float roadInner = track.width * 0.5f - 1f; // dip fully this far in from the wall...
            float roadEdge = track.width * 0.5f;       // ...and ramp back up to the wall base
            var vertices = new Vector3[cols * rows];
            var uvs = new Vector2[cols * rows];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var flat = new Vector3(min.x + c * cell, 0f, min.y + r * cell);
                    float y = track.GroundHeightAt(flat, out float lateral);
                    if (lateral < roadEdge)
                    {
                        float t = Mathf.InverseLerp(roadEdge, roadInner, lateral);
                        y -= UnderRoadDip * t;
                    }
                    vertices[r * cols + c] = new Vector3(flat.x, y, flat.z);
                    uvs[r * cols + c] = new Vector2(flat.x, flat.z) / TerrainUvMetres;
                }
            }

            var triangles = new int[(cols - 1) * (rows - 1) * 6];
            int t3 = 0;
            for (int r = 0; r < rows - 1; r++)
            {
                for (int c = 0; c < cols - 1; c++)
                {
                    int a = r * cols + c, b = a + 1, d = a + cols, e = d + 1;
                    // Clockwise seen from above (Unity's front face).
                    triangles[t3++] = a; triangles[t3++] = d; triangles[t3++] = b;
                    triangles[t3++] = b; triangles[t3++] = d; triangles[t3++] = e;
                }
            }

            var mesh = new Mesh { name = "Terrain", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Terrain");
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.SetParent(root, worldPositionStays: true);
            go.isStatic = true;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = groundMaterial != null
                ? groundMaterial
                : ToonStyle.Create(new Color(0.35f, 0.45f, 0.3f), outline: false, name: "Terrain");
            // Receive only, like the ground plane it extends.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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

            CreateMeshObject(root, "CenterLine", dashes, paint, withCollider: false, outline: false).isStatic = true;
            CreateMeshObject(root, "EdgeLines", edges, paint, withCollider: false, outline: false).isStatic = true;
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

            CreateMeshObject(root, "Curb_A", a, white, withCollider: false, outline: false).isStatic = true;
            CreateMeshObject(root, "Curb_B", b, accent, withCollider: false, outline: false).isStatic = true;
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

            var go = CreateMeshObject(root, name, builder, color, withCollider: true, outline: true);
            go.isStatic = true;
            go.AddComponent<TrackBarrier>();
        }

        private static GameObject CreateMeshObject(Transform root, string name, MeshBuilder builder, Color color, bool withCollider, bool outline = true)
        {
            // Vertices are in world space, so the object must sit at world
            // identity no matter how the root happens to be transformed.
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.SetParent(root, worldPositionStays: true);

            Mesh mesh = builder.ToMesh(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ToonStyle.Create(color, outline, name: name);

            if (withCollider)
            {
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
            return go;
        }

        /// <summary>
        /// Closed Catmull-Rom spline through the control points, so the road
        /// and walls curve instead of kinking at every control point. Also
        /// smooths the elevation, so hills roll instead of creasing. Exposed
        /// for GeneratedTrack.Samples, which everything else queries.
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
