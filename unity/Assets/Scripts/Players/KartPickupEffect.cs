using UnityEngine;

namespace MarioKart.Players
{
    /// <summary>
    /// Cartoon feedback when a kart drives through a track pickup, keyed off
    /// KartController.PowerUp / PowerDown (the same hooks KartAudio uses):
    ///   • boost: a gold ring bursts out along the road, stars scatter, and
    ///     a sparkle trail streams off the back for as long as the boost lasts,
    ///   • stop: a red ring and a spray of red "X" marks popping up off the kart,
    ///   • spin: a blue ring and a halo of dizzy stars orbiting the driver's
    ///     head while the kart is turning.
    /// Everything is built in code from KartParticles shapes (ADR 0005) in
    /// the same outlined, stepped-colour, shrink-out style as the other kart
    /// effects (ADR 0008).
    /// </summary>
    [RequireComponent(typeof(KartController))]
    public class KartPickupEffect : MonoBehaviour
    {
        [Header("Boost")]
        public Color boostColor = new Color(1f, 0.88f, 0.25f);
        public Color boostShade = new Color(1f, 0.55f, 0.1f);
        public Color boostRingColor = new Color(1f, 0.93f, 0.5f);
        [Tooltip("Stars thrown out the moment the boost lands.")]
        public int boostBurstCount = 16;
        [Tooltip("Sparkles per second streaming off the back while boosting.")]
        public float boostTrailRate = 45f;

        [Header("Stop")]
        public Color stopColor = new Color(0.92f, 0.2f, 0.16f);
        public Color stopShade = new Color(0.6f, 0.1f, 0.1f);
        public int stopCrossCount = 7;

        [Header("Spin")]
        public Color dizzyColor = new Color(0.8f, 0.88f, 1f);
        public Color dizzyShade = new Color(0.5f, 0.62f, 0.92f);
        public int dizzyStarCount = 3;
        public float dizzyOrbitRadius = 0.8f;
        [Tooltip("Degrees per second the dizzy stars circle the driver's head.")]
        public float dizzyOrbitSpeed = 540f;
        [Tooltip("The halo lingers this long after a short spin so it always reads.")]
        public float dizzyMinDuration = 0.6f;

        // Local-space points on the unit cube the kart is built from (scaled
        // 1.6 × 0.6 × 2.6 by the scene builder): above the driver, and the
        // rear bumper.
        private static readonly Vector3 HeadLocal = new Vector3(0f, 0.5f, -0.1f);
        private static readonly Vector3 RearLocal = new Vector3(0f, 0f, -0.55f);
        private const float HeadClearance = 1.05f; // metres above HeadLocal for the halo

        private KartController kart;
        private ParticleSystem ring, boostStars, boostTrail, stopCrosses, dizzyStars;
        private Texture2D starTexture, ringTexture, crossTexture;
        private Material starMaterial, ringMaterial, crossMaterial;
        private float dizzyUntil;

        private void Awake()
        {
            kart = GetComponent<KartController>();

            starTexture = KartParticles.Star();
            ringTexture = KartParticles.Ring();
            crossTexture = KartParticles.Cross();
            starMaterial = KartParticles.CreateMaterial("KartPickupStar", starTexture);
            ringMaterial = KartParticles.CreateMaterial("KartPickupRing", ringTexture);
            crossMaterial = KartParticles.CreateMaterial("KartPickupCross", crossTexture);

            ring = CreateRing();
            boostStars = CreateStars("BoostStars", boostColor, boostShade, 0.35f, 0.55f, 0.45f, 0.7f, gravity: 0.6f);
            boostTrail = CreateBoostTrail();
            stopCrosses = CreateCrosses();
            dizzyStars = CreateStars("DizzyStars", dizzyColor, dizzyShade, 0.3f, 0.4f, 0.1f, 0.14f, gravity: 0f);
        }

        private void OnEnable()
        {
            kart.PowerUp += OnPowerUp;
            kart.PowerDown += OnPowerDown;
        }

        private void OnDisable()
        {
            kart.PowerUp -= OnPowerUp;
            kart.PowerDown -= OnPowerDown;
        }

        private void OnDestroy()
        {
            foreach (var m in new[] { starMaterial, ringMaterial, crossMaterial })
            {
                if (m != null) Destroy(m);
            }
            foreach (var t in new[] { starTexture, ringTexture, crossTexture })
            {
                if (t != null) Destroy(t);
            }
        }

        private void Update()
        {
            var trail = boostTrail.emission;
            trail.rateOverTime = kart.IsBoosting ? boostTrailRate : 0f;

            if (kart.IsSpinning || Time.time < dizzyUntil)
            {
                EmitDizzyHalo();
            }
        }

        // ------------------------------------------------------------------

        private void OnPowerUp()
        {
            EmitRing(boostRingColor);

            // Stars thrown up and back, so the kart visibly leaves them behind
            // as it takes off. Cosmetic jitter only, so UnityEngine.Random is
            // fine here (ADR 0004 is about world generation).
            var emit = new ParticleSystem.EmitParams();
            Vector3 origin = transform.TransformPoint(new Vector3(0f, 0.2f, 0f));
            for (int i = 0; i < boostBurstCount; i++)
            {
                emit.position = origin + Random.insideUnitSphere * 0.3f;
                emit.velocity = (Vector3.up * 2.5f - transform.forward * 3f + Random.insideUnitSphere * 3.5f) * Random.Range(0.8f, 1.3f);
                boostStars.Emit(emit, 1);
            }
        }

        private void OnPowerDown()
        {
            // KartController sets its state before raising the event, so the
            // flags tell the two power-downs apart.
            if (kart.IsSpinning)
            {
                EmitRing(dizzyColor);
                dizzyUntil = Time.time + dizzyMinDuration;
            }
            else
            {
                EmitRing(stopColor);
                EmitCrosses();
            }
        }

        /// <summary>One flat ring on the road under the kart that swells and vanishes.</summary>
        private void EmitRing(Color color)
        {
            Vector3 ground = transform.position;
            ground.y = transform.position.y - transform.localScale.y * 0.5f + 0.05f; // kart's underside, above the road
            var emit = new ParticleSystem.EmitParams
            {
                position = ground,
                velocity = Vector3.zero,
                startSize = 1f,
                rotation = 0f,
                startColor = color,
            };
            ring.Emit(emit, 1);
        }

        /// <summary>Red X marks pop up off the kart and drift upward.</summary>
        private void EmitCrosses()
        {
            var emit = new ParticleSystem.EmitParams();
            Vector3 origin = transform.TransformPoint(new Vector3(0f, 0.5f, 0f));
            for (int i = 0; i < stopCrossCount; i++)
            {
                Vector3 spread = Random.insideUnitSphere;
                spread.y = Mathf.Abs(spread.y) * 0.3f;
                emit.position = origin + spread * 0.7f;
                emit.velocity = Vector3.up * Random.Range(1.2f, 2.2f) + spread * 1.2f;
                stopCrosses.Emit(emit, 1);
            }
        }

        /// <summary>
        /// A rotating ring of short-lived stars over the driver's head: each
        /// frame places the stars a little further round, and the previous
        /// frame's stars are gone by the time the next lands.
        /// </summary>
        private void EmitDizzyHalo()
        {
            Vector3 centre = transform.TransformPoint(HeadLocal) + Vector3.up * HeadClearance;
            float baseAngle = Time.time * dizzyOrbitSpeed * Mathf.Deg2Rad;
            var emit = new ParticleSystem.EmitParams { velocity = Vector3.zero };
            for (int i = 0; i < dizzyStarCount; i++)
            {
                float angle = baseAngle + i * Mathf.PI * 2f / dizzyStarCount;
                emit.position = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dizzyOrbitRadius;
                emit.rotation = angle * Mathf.Rad2Deg;
                dizzyStars.Emit(emit, 1);
            }
        }

        // ------------------------------------------------------------------

        private ParticleSystem CreateRing()
        {
            var ps = KartParticles.CreateSystem(transform, "PickupRing", ringMaterial, 8);
            var main = ps.main;
            main.startLifetime = 0.35f;
            main.startColor = Color.white; // set per emit
            main.gravityModifier = 0f;

            // Swells from under the kart to well past its wheels, then is
            // simply gone -- no fade.
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = KartParticles.Grow(0.8f, 4.5f);

            // Lies flat on the road.
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;

            ps.Play();
            return ps;
        }

        private ParticleSystem CreateStars(string name, Color color, Color shade, float minSize, float maxSize, float minLife, float maxLife, float gravity)
        {
            var ps = KartParticles.CreateSystem(transform, name, starMaterial, 128);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(minLife, maxLife);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startColor = Color.white; // colour comes from the bands below
            main.gravityModifier = gravity;

            var colorModule = ps.colorOverLifetime;
            colorModule.enabled = true;
            colorModule.color = KartParticles.Steps(color, shade);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = KartParticles.PopAndShrink(overshoot: 1.4f, peakAt: 0.1f, holdUntil: 0.45f);

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-360f * Mathf.Deg2Rad, 360f * Mathf.Deg2Rad);

            ps.Play();
            return ps;
        }

        /// <summary>Small gold sparkles streaming off the rear bumper while boosting.</summary>
        private ParticleSystem CreateBoostTrail()
        {
            var ps = KartParticles.CreateSystem(transform, "BoostTrail", starMaterial, 256);
            ps.transform.localPosition = RearLocal;
            ps.transform.localRotation = Quaternion.LookRotation(Vector3.back); // emit out the rear

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
            main.startColor = Color.white;
            main.gravityModifier = 0.15f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 25f;
            shape.radius = 0.5f;

            var colorModule = ps.colorOverLifetime;
            colorModule.enabled = true;
            colorModule.color = KartParticles.Steps(Color.white, boostColor, boostShade);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = KartParticles.PopAndShrink(overshoot: 1.3f, peakAt: 0.1f, holdUntil: 0.4f);

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-540f * Mathf.Deg2Rad, 540f * Mathf.Deg2Rad);

            ps.Play();
            return ps;
        }

        private ParticleSystem CreateCrosses()
        {
            var ps = KartParticles.CreateSystem(transform, "StopCrosses", crossMaterial, 32);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.65f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.6f);
            main.startColor = Color.white;
            main.gravityModifier = -0.1f; // keeps floating up

            var colorModule = ps.colorOverLifetime;
            colorModule.enabled = true;
            colorModule.color = KartParticles.Steps(stopColor, stopShade);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = KartParticles.PopAndShrink(overshoot: 1.5f, peakAt: 0.12f, holdUntil: 0.55f);

            // A slow wobble rather than a spin, so they still read as X's.
            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-60f * Mathf.Deg2Rad, 60f * Mathf.Deg2Rad);

            ps.Play();
            return ps;
        }
    }
}
