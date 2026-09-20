using MarioKart.Players;
using UnityEngine;

namespace MarioKart.World
{
    public enum ObstacleKind
    {
        Boost,
        Paralyze,
        Spin,
    }

    /// <summary>
    /// Pass-through pickup on the racing line. Trigger only — no physics
    /// collision — so karts drive through it. Each kind applies a short
    /// KartController effect; collecting destroys this instance and asks
    /// ObstacleGenerator to spawn a replacement elsewhere on the track.
    /// </summary>
    public class TrackObstacle : MonoBehaviour
    {
        public ObstacleKind kind = ObstacleKind.Boost;

        [Header("Boost")]
        public float boostDuration = 2.5f;
        public float boostMultiplier = 1.55f;

        [Header("Paralyze")]
        public float paralyzeDuration = 0.5f;

        [Header("Spin")]
        [Tooltip("Inclusive range of full 360° turns applied on hit.")]
        public float minSpinTurns = 0.35f;
        public float maxSpinTurns = 1.15f;

        [Header("Beacon")]
        [Tooltip("Seconds between each ground-ring pulse.")]
        public float beaconPulseInterval = 0.7f;
        public float beaconMinSize = 0.9f;
        public float beaconMaxSize = 2.6f;

        private const float BobAmplitude = 0.12f;
        private const float BobSpeed = 2.4f;

        private ObstacleGenerator owner;
        private Vector3 restPosition;
        private float bobPhase;
        private float hoverHeight;
        private bool consumed;

        public void Configure(ObstacleKind obstacleKind, float phase, ObstacleGenerator generator, float hoverHeightAboveRoad)
        {
            kind = obstacleKind;
            bobPhase = phase;
            owner = generator;
            hoverHeight = hoverHeightAboveRoad;
            restPosition = transform.position;
            consumed = false;
            BuildBeacon();
        }

        private void Awake()
        {
            restPosition = transform.position;
        }

        /// <summary>
        /// A colour-coded ring (green/red/yellow -- see ObstacleVisuals.
        /// ColorFor) that pulses outward from directly under the pickup and
        /// fades, on a continuous loop for as long as it sits uncollected.
        /// Meant to be noticed from much further away than the sign
        /// geometry: a driver approaching at speed reads the colour long
        /// before the shape.
        /// </summary>
        private void BuildBeacon()
        {
            var ps = KartParticles.CreateSystem(transform, "Beacon", ObstacleVisuals.BeaconMaterial(), 4);
            // Sits at ground height directly under the (bobbing) pickup,
            // just proud of the road surface to avoid z-fighting.
            ps.transform.localPosition = new Vector3(0f, -hoverHeight + 0.03f, 0f);

            var main = ps.main;
            main.startLifetime = beaconPulseInterval;
            main.startSize = beaconMinSize;
            main.startSpeed = 0f; // stays put and grows in place, never flies off
            main.startColor = Color.white; // tinted below, per kind
            main.gravityModifier = 0f;

            // A fresh ParticleSystem defaults to an enabled Cone shape, which
            // (combined with the default startSpeed of 5) would fling every
            // burst particle off in a random cone direction instead of
            // letting it sit still and pulse -- KartPickupEffect avoids this
            // by emitting manually with EmitParams.velocity = Vector3.zero,
            // but automatic bursts (used below) have no such override, so
            // the shape module must be disabled explicitly here instead.
            var shape = ps.shape;
            shape.enabled = false;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            // One ring, repeating forever, exactly as often as it fades out.
            // Set via properties rather than a specific Burst(...) overload,
            // since the constructor overload set (short count vs. a
            // MinMaxCurve count) differs across Unity versions.
            var burst = new ParticleSystem.Burst(0f, 1);
            burst.cycleCount = 0;
            burst.repeatInterval = beaconPulseInterval;
            emission.SetBursts(new[] { burst });

            var colorModule = ps.colorOverLifetime;
            colorModule.enabled = true;
            Color color = ObstacleVisuals.ColorFor(kind);
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
            colorModule.color = gradient;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = KartParticles.Grow(1f, beaconMaxSize / beaconMinSize);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard; // lies flat, facing up

            ps.Play();
        }

        private void Update()
        {
            if (consumed) return;
            transform.position = restPosition + Vector3.up * (Mathf.Sin(Time.time * BobSpeed + bobPhase) * BobAmplitude);
            transform.Rotate(0f, 70f * Time.deltaTime, 0f, Space.World);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (consumed) return;
            var kart = other.GetComponentInParent<KartController>();
            if (kart == null) return;

            consumed = true;
            ApplyTo(kart);
            if (owner != null) owner.OnCollected(this);
            else Destroy(gameObject);
        }

        private void ApplyTo(KartController kart)
        {
            switch (kind)
            {
                case ObstacleKind.Boost:
                    kart.ApplyBoost(boostDuration, boostMultiplier);
                    break;
                case ObstacleKind.Paralyze:
                    kart.ApplyParalyze(paralyzeDuration);
                    break;
                case ObstacleKind.Spin:
                    float turns = Random.Range(minSpinTurns, maxSpinTurns);
                    if (Random.value < 0.5f) turns = -turns;
                    kart.ApplySpin(turns);
                    break;
            }
        }
    }
}
