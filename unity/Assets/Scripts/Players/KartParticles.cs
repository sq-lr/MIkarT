using UnityEngine;

namespace MarioKart.Players
{
    /// <summary>
    /// Shared helpers for the karts' code-built particle effects (exhaust,
    /// skid). Nothing here is an asset: textures and materials are created
    /// at runtime so no prefab or .mat needs hand-authoring (ADR 0005).
    /// </summary>
    public static class KartParticles
    {
        /// <summary>Radial soft-edged white disc with alpha falloff.</summary>
        public static Texture2D SoftCircle(int size = 32)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "SoftCircle",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.25f, 1f, d));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Additive for glowing things (fire, sparks); alpha-blended for
        /// smoke. Both fall back to Sprites/Default, which is always included
        /// in builds, if the legacy particle shaders are missing.
        /// </summary>
        public static Material CreateMaterial(string name, Texture2D texture, bool additive)
        {
            string preferred = additive ? "Legacy Shaders/Particles/Additive" : "Legacy Shaders/Particles/Alpha Blended";
            var shader = Shader.Find(preferred) ?? Shader.Find("Sprites/Default");
            return new Material(shader) { name = name, mainTexture = texture };
        }

        /// <summary>
        /// A world-space particle system with no automatic emission, ready
        /// for Emit() bursts or a rateOverTime set by the caller.
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

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.sortingFudge = -10f; // draw in front of the road

            return ps;
        }

        public static Gradient FadeOut(float holdUntil = 0.3f)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, holdUntil), new GradientAlphaKey(0f, 1f) });
            return gradient;
        }
    }
}
