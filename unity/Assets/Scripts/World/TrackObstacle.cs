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

        private const float BobAmplitude = 0.12f;
        private const float BobSpeed = 2.4f;

        private ObstacleGenerator owner;
        private Vector3 restPosition;
        private float bobPhase;
        private bool consumed;

        public void Configure(ObstacleKind obstacleKind, float phase, ObstacleGenerator generator)
        {
            kind = obstacleKind;
            bobPhase = phase;
            owner = generator;
            restPosition = transform.position;
            consumed = false;
        }

        private void Awake()
        {
            restPosition = transform.position;
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

        public static Color KindColor(ObstacleKind kind)
        {
            switch (kind)
            {
                case ObstacleKind.Boost: return new Color(0.98f, 0.82f, 0.42f);
                case ObstacleKind.Paralyze: return new Color(0.78f, 0.42f, 0.38f);
                case ObstacleKind.Spin: return new Color(0.62f, 0.68f, 0.82f);
                default: return Color.white;
            }
        }
    }
}
