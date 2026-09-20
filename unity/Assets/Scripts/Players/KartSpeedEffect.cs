using UnityEngine;

namespace MarioKart.Players
{
    /// <summary>
    /// Exhaust flame trails that ignite as the kart nears its top speed.
    /// Reads KartController.ForwardSpeed every frame and scales the
    /// emission rate from 0 at `activationFraction × maxSpeed` up to full at
    /// maxSpeed -- so backing off the throttle, or scraping a barrier (which
    /// caps top speed), visibly kills the flames.
    /// </summary>
    [RequireComponent(typeof(KartController))]
    public class KartSpeedEffect : MonoBehaviour
    {
        [Tooltip("Fraction of maxSpeed at which the effect starts to appear.")]
        [Range(0f, 1f)] public float activationFraction = 0.85f;
        [Tooltip("Particles per second per exhaust at full speed.")]
        public float maxEmissionRate = 70f;
        // Flat colour bands each flame blob steps through over its life
        // (white-hot at the pipe, dark red as it dies).
        public Color flameCore = new Color(1f, 0.98f, 0.85f);
        public Color flameMid = new Color(1f, 0.85f, 0.2f);
        public Color flameEdge = new Color(1f, 0.45f, 0.1f);
        public Color flameTail = new Color(0.55f, 0.12f, 0.08f);

        // Local-space exhaust positions on the unit cube the kart is built
        // from (scaled 1.6 × 0.6 × 2.6 by the scene builder).
        private static readonly Vector3[] ExhaustLocalPositions =
        {
            new Vector3(-0.3f, -0.2f, -0.55f),
            new Vector3(0.3f, -0.2f, -0.55f),
        };

        private KartController kart;
        private ParticleSystem[] exhausts;
        private Material material;
        private Texture2D texture;

        private void Awake()
        {
            kart = GetComponent<KartController>();

            texture = KartParticles.HardCircle();
            material = KartParticles.CreateMaterial("KartExhaust", texture);

            exhausts = new ParticleSystem[ExhaustLocalPositions.Length];
            for (int i = 0; i < exhausts.Length; i++)
            {
                exhausts[i] = CreateExhaust($"Exhaust_{i}", ExhaustLocalPositions[i]);
            }
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
            if (texture != null) Destroy(texture);
        }

        private void Update()
        {
            float start = kart.maxSpeed * activationFraction;
            float intensity = Mathf.InverseLerp(start, kart.maxSpeed, kart.ForwardSpeed);
            float rate = intensity * maxEmissionRate;

            foreach (var ps in exhausts)
            {
                var emission = ps.emission;
                emission.rateOverTime = rate;
            }
        }

        private ParticleSystem CreateExhaust(string name, Vector3 localPosition)
        {
            var ps = KartParticles.CreateSystem(transform, name, material, 256);
            ps.transform.localPosition = localPosition;
            ps.transform.localRotation = Quaternion.LookRotation(Vector3.back); // emit out the rear

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.startColor = Color.white; // colour comes from the bands below

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10f;
            shape.radius = 0.08f;

            var color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = KartParticles.Steps(flameCore, flameMid, flameEdge, flameTail);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = KartParticles.PopAndShrink(overshoot: 1.15f, peakAt: 0.1f, holdUntil: 0.35f);

            ps.Play();
            return ps;
        }
    }
}
