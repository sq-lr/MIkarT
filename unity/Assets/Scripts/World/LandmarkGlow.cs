using UnityEngine;

namespace MarioKart.World
{
    /// <summary>
    /// Emphasis effect for a landmark: a pulsing warm light and a light
    /// sparkle drift upward -- so a landmark reads as a deliberate
    /// centrepiece instead of blending into the (now much more prominent)
    /// background wall behind it. Purely decorative: no collider, no
    /// gameplay effect, added once by EnvironmentGenerator right after a
    /// landmark placeholder spawns.
    /// </summary>
    public class LandmarkGlow : MonoBehaviour
    {
        public float pulseSpeed = 2f;
        public float pulseAmount = 0.2f;

        private Light glowLight;
        private float baseIntensity;

        private void Awake()
        {
            glowLight = GetComponentInChildren<Light>();
            if (glowLight != null) baseIntensity = glowLight.intensity;
        }

        private void Update()
        {
            if (glowLight != null)
            {
                glowLight.intensity = baseIntensity * (1f + pulseAmount * Mathf.Sin(Time.time * pulseSpeed));
            }
        }

        /// <summary>Builds and attaches the whole effect under `landmark`, sized off its rendered footprint/height.</summary>
        public static void Attach(GameObject landmark)
        {
            if (landmark == null) return;
            var renderer = landmark.GetComponent<Renderer>();
            float footprint = renderer != null ? Mathf.Max(renderer.bounds.size.x, renderer.bounds.size.z) : 2f;
            float height = renderer != null ? renderer.bounds.size.y : 2f;

            var root = new GameObject("Glow");
            root.transform.SetParent(landmark.transform, worldPositionStays: false);
            root.transform.localPosition = Vector3.zero;
            root.AddComponent<LandmarkGlow>();

            AddLight(root.transform, height);
            AddSparkle(root.transform, footprint, height);
        }

        private static void AddLight(Transform parent, float height)
        {
            var lightObject = new GameObject("GlowLight");
            lightObject.transform.SetParent(parent, worldPositionStays: false);
            lightObject.transform.localPosition = new Vector3(0f, height * 0.55f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.86f, 0.35f);
            light.intensity = 3f;
            light.range = Mathf.Max(height, 4f) * 4f;
        }

        private static void AddSparkle(Transform parent, float footprint, float height)
        {
            var go = new GameObject("Sparkle");
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = new Vector3(0f, height * 0.15f, 0f);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 1.4f;
            main.startSpeed = 0.6f;
            main.startSize = 0.18f;
            main.startColor = new Color(1f, 0.92f, 0.5f, 0.9f);
            main.gravityModifier = -0.05f; // drifts gently upward
            main.maxParticles = 40;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = ps.emission;
            emission.rateOverTime = 10f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Mathf.Max(footprint * 0.4f, 0.5f);

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.8f, 0.2f), 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            var shader = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Sprites/Default");
            renderer.sharedMaterial = new Material(shader) { name = "SparkleMat", color = new Color(1f, 0.9f, 0.4f, 0.8f) };
        }
    }
}
