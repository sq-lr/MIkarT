using UnityEngine;

namespace MarioKart.Players
{
    /// <summary>
    /// Shared helpers for the karts' code-built particle effects (exhaust,
    /// skid, impact poof). Nothing here is an asset: textures and materials
    /// are created at runtime so no prefab or .mat needs hand-authoring
    /// (ADR 0005).
    ///
    /// The look is deliberately cartoon, to match the toon-shaded world
    /// (ADR 0008): hard-edged outlined shapes rather than soft discs, flat
    /// stepped colours rather than gradients, and particles that pop in and
    /// shrink out rather than fading to transparent. Particles stay unlit --
    /// they should not pick up the world's lighting bands.
    /// </summary>
    public static class KartParticles
    {
        /// <summary>Dark ink line around every shape, multiplied by the particle colour.</summary>
        private static readonly Color Outline = new Color(0.12f, 0.10f, 0.14f, 1f);

        /// <summary>Fraction of the shape's radius taken up by the outline.</summary>
        private const float OutlineFraction = 0.14f;

        /// <summary>Solid white disc with a dark outline; alpha is 0 or 1, no falloff.</summary>
        public static Texture2D HardCircle(int size = 64)
        {
            return Shape("HardCircle", size, (x, y) => Mathf.Sqrt(x * x + y * y));
        }

        /// <summary>Four-point star (cartoon spark) with a dark outline.</summary>
        public static Texture2D Star(int size = 64)
        {
            // Distance field for a 4-point star: an astroid-like shape where
            // the "radius" grows toward the diagonals so points land on the
            // axes.
            return Shape("Star", size, (x, y) =>
            {
                float ax = Mathf.Abs(x), ay = Mathf.Abs(y);
                return Mathf.Pow(Mathf.Pow(ax, 0.5f) + Mathf.Pow(ay, 0.5f), 2f);
            });
        }

        /// <summary>Ring (impact "poof") with a dark outline on both edges.</summary>
        public static Texture2D Ring(int size = 64, float innerRadius = 0.55f)
        {
            // Map the annulus onto 0..1 so the shared outline logic draws a
            // line on the inside edge as well as the outside.
            return Shape("Ring", size, (x, y) =>
            {
                float d = Mathf.Sqrt(x * x + y * y);
                float band = (d - innerRadius) / (1f - innerRadius);   // 0 at inner edge, 1 at outer
                return Mathf.Abs(band * 2f - 1f);                      // 1 at both edges, 0 mid-ring
            });
        }

        /// <summary>
        /// Rasterise a shape from a normalised distance function (0 at the
        /// centre, 1 at the edge, >1 outside): white fill, dark outline just
        /// inside the edge, transparent outside.
        /// </summary>
        private static Texture2D Shape(string name, int size, System.Func<float, float, float> distance)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color[size * size];
            float half = size * 0.5f;
            float fillEdge = 1f - OutlineFraction;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = distance((x + 0.5f - half) / half, (y + 0.5f - half) / half);
                    Color c;
                    if (d <= fillEdge) c = Color.white;
                    else if (d <= 1f) c = Outline;
                    else c = Color.clear;
                    pixels[y * size + x] = c;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Alpha-blended, unlit. Everything -- including flames and sparks --
        /// is alpha-blended rather than additive: additive glow is the
        /// realistic-fire look, and it washes out the outlines. Falls back to
        /// Sprites/Default, which is always included in builds.
        /// </summary>
        public static Material CreateMaterial(string name, Texture2D texture)
        {
            var shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Sprites/Default");
            return new Material(shader) { name = name, mainTexture = texture };
        }

        /// <summary>
        /// A world-space particle system with no automatic emission, ready
        /// for Emit() bursts or a rateOverTime set by the caller. Every
        /// particle spawns at a random spin so repeated shapes don't line up.
        /// </summary>
        public static ParticleSystem CreateSystem(Transform parent, string name, Material material, int maxParticles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local; // ignore the kart's non-uniform scale
            main.maxParticles = maxParticles;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.sortingFudge = -10f; // draw in front of the road

            return ps;
        }

        /// <summary>
        /// Flat colour bands over a particle's life with no blending between
        /// them, alpha fixed at 1: colour i covers the i-th slice of the
        /// lifetime. (In GradientMode.Fixed a key's colour runs up to that
        /// key's time.)
        /// </summary>
        public static Gradient Steps(params Color[] colors)
        {
            int n = Mathf.Max(colors.Length, 1);
            var colorKeys = new GradientColorKey[n];
            for (int i = 0; i < n; i++)
            {
                colorKeys[i] = new GradientColorKey(colors[Mathf.Min(i, colors.Length - 1)], (i + 1f) / n);
            }
            var gradient = new Gradient { mode = GradientMode.Fixed };
            gradient.SetKeys(colorKeys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }

        /// <summary>
        /// Size over lifetime that pops in past full size, settles, then
        /// shrinks to nothing -- cartoon particles disappear by shrinking,
        /// never by fading.
        /// </summary>
        public static ParticleSystem.MinMaxCurve PopAndShrink(float overshoot = 1.25f, float peakAt = 0.12f, float holdUntil = 0.5f)
        {
            var curve = new AnimationCurve(
                new Keyframe(0f, 0.5f),
                new Keyframe(peakAt, overshoot),
                new Keyframe(holdUntil, 1f),
                new Keyframe(1f, 0f));
            return new ParticleSystem.MinMaxCurve(1f, curve);
        }

        /// <summary>Size over lifetime that swells from `from` to `to` and is then cut off.</summary>
        public static ParticleSystem.MinMaxCurve Grow(float from, float to)
        {
            return new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, from, 1f, to));
        }
    }
}
