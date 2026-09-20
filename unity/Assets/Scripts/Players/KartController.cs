using MarioKart.InputSystem;
using UnityEngine;

namespace MarioKart.Players
{
    /// <summary>
    /// Arcade kart physics. Velocity-driven rather than force-driven so the
    /// kart moves predictably regardless of ground friction, and steering is
    /// scaled by speed so the kart turns (and slides a little across the
    /// track) while moving but never spins in place. Still deliberately
    /// simple: no drift button, suspension, or collision tuning.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class KartController : MonoBehaviour
    {
        [SerializeField] private Rigidbody rb;

        [Header("Speed (m/s, m/s²)")]
        public float maxSpeed = 20f;
        public float maxReverseSpeed = 6f;
        public float acceleration = 12f;
        public float brakeForce = 20f;
        public float coastDeceleration = 4f;

        [Header("Steering")]
        [Tooltip("Yaw rate in deg/s at full speed. Scales down to 0 when stationary.")]
        public float steerSpeed = 110f;
        [Tooltip("How quickly sideways velocity is bled off. Lower = more slide through corners.")]
        public float grip = 6f;

        [Header("Barriers")]
        [Tooltip("Fraction of speed lost the moment the kart hits a barrier wall.")]
        [Range(0f, 1f)] public float barrierHitSpeedLoss = 0.4f;
        [Tooltip("Top speed while scraping along a barrier, as a fraction of maxSpeed.")]
        [Range(0f, 1f)] public float barrierScrapeSpeedFactor = 0.5f;
        [Tooltip("Fraction of collision-induced spin that survives the moment of impact.")]
        [Range(0f, 1f)] public float barrierHitSpinKeep = 0.2f;

        [Header("Skid")]
        [Tooltip("Grip multiplier while skidding (after hitting a wall or the other kart). Lower = longer slide.")]
        [Range(0f, 1f)] public float skidGripFactor = 0.15f;

        [Header("Spin control")]
        [Tooltip("How fast physics-induced spin (from walls / the other kart) is bled off, per second. Higher = stops sooner.")]
        public float spinDamping = 12f;
        [Tooltip("Cap on collision-induced spin, in deg/s. Lower = spins less.")]
        public float maxCollisionSpin = 60f;

        private KartInput currentInput;
        private PhysicsMaterial frictionless;
        private float scrapingUntil; // Time.time until which the scrape cap applies
        private float skidUntil;     // Time.time until which grip is reduced

        public bool IsSkidding => Time.time < skidUntil;

        public float ForwardSpeed { get; private set; }

        /// <summary>Current steering input in [-1, 1] (read by KartVisual to turn the front wheels).</summary>
        public float Steering => currentInput.steering;

        /// <summary>
        /// Raised on first contact with anything solid -- a barrier wall or
        /// the other kart -- with the impact speed in m/s (the kart's own
        /// speed against a wall, the closing speed against another kart).
        /// PlayerCamera uses it to shake.
        /// </summary>
        public event System.Action<float> Impact;

        private void Awake()
        {
            if (rb == null) rb = GetComponent<Rigidbody>();

            // We own horizontal velocity entirely, so ground friction would
            // only fight us. Zero it on the kart's own collider.
            frictionless = new PhysicsMaterial("Kart (frictionless)")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };
            foreach (var col in GetComponentsInChildren<Collider>())
            {
                col.material = frictionless;
            }
        }

        private void OnDestroy()
        {
            if (frictionless != null) Destroy(frictionless);
        }

        public void ApplyInput(KartInput input)
        {
            currentInput = input;
        }

        /// <summary>
        /// Drop grip for a while so the kart slides instead of snapping back
        /// in line. Called by KartSkidEffect on any solid contact.
        /// </summary>
        public void StartSkid(float seconds)
        {
            skidUntil = Mathf.Max(skidUntil, Time.time + seconds);
        }

        /// <summary>Called by TrackBarrier on first contact with a wall.</summary>
        public void OnBarrierHit()
        {
            if (rb.isKinematic) return;
            float impactSpeed = rb.linearVelocity.magnitude;
            rb.linearVelocity *= 1f - barrierHitSpeedLoss;
            rb.angularVelocity *= barrierHitSpinKeep;
            OnBarrierScrape();
            Impact?.Invoke(impactSpeed);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (rb.isKinematic) return;
            // Walls report through OnBarrierHit (TrackBarrier calls it); this
            // covers everything else that is solid, i.e. the other kart.
            if (collision.collider.GetComponent<MarioKart.World.TrackBarrier>() != null) return;

            float closingSpeed = collision.relativeVelocity.magnitude;
            if (closingSpeed < 1f) return; // resting / nudging contact, no shake
            Impact?.Invoke(closingSpeed);
        }

        /// <summary>Called by TrackBarrier every physics step the kart touches a wall.</summary>
        public void OnBarrierScrape()
        {
            // Collision callbacks run after the physics step, so keep the cap
            // alive slightly longer than one step to avoid flickering.
            scrapingUntil = Time.time + Time.fixedDeltaTime * 2f;
        }

        private void FixedUpdate()
        {
            if (rb.isKinematic) // frozen by RaceBootstrap (lobby / countdown / results)
            {
                ForwardSpeed = 0f;
                return;
            }

            float dt = Time.fixedDeltaTime;
            Vector3 velocity = rb.linearVelocity;
            Vector3 forward = transform.forward;
            Vector3 right = transform.right;

            float forwardSpeed = Vector3.Dot(velocity, forward);
            float lateralSpeed = Vector3.Dot(velocity, right);

            // ---- Longitudinal ----
            float topSpeed = Time.time < scrapingUntil ? maxSpeed * barrierScrapeSpeedFactor : maxSpeed;
            if (currentInput.throttle > 0f)
            {
                forwardSpeed = Mathf.MoveTowards(forwardSpeed, topSpeed * currentInput.throttle, acceleration * dt);
            }
            else if (currentInput.brake > 0f)
            {
                // Brake to a stop, then reverse.
                float target = forwardSpeed > 0.05f ? 0f : -maxReverseSpeed * currentInput.brake;
                forwardSpeed = Mathf.MoveTowards(forwardSpeed, target, brakeForce * dt);
            }
            else
            {
                forwardSpeed = Mathf.MoveTowards(forwardSpeed, 0f, coastDeceleration * dt);
            }

            // ---- Spin control: we own yaw, so collision spin is a nuisance ----
            // Bleed it off fast and cap what's left so a wall bump nudges the
            // heading instead of pirouetting the kart (and the camera).
            Vector3 spin = rb.angularVelocity * Mathf.Max(0f, 1f - spinDamping * dt);
            float maxSpinRad = maxCollisionSpin * Mathf.Deg2Rad;
            if (spin.sqrMagnitude > maxSpinRad * maxSpinRad) spin = spin.normalized * maxSpinRad;
            rb.angularVelocity = spin;

            // ---- Steering: yaw rate scales with speed, flips in reverse ----
            float speedFactor = Mathf.Clamp(forwardSpeed / maxSpeed, -1f, 1f);
            float yaw = currentInput.steering * steerSpeed * speedFactor * dt;
            if (Mathf.Abs(yaw) > 0f)
            {
                rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, yaw, 0f));
                forward = rb.rotation * Vector3.forward;
                right = rb.rotation * Vector3.right;
            }

            // ---- Lateral grip: bleed off sideways slide ----
            float effectiveGrip = IsSkidding ? grip * skidGripFactor : grip;
            lateralSpeed = Mathf.MoveTowards(lateralSpeed, 0f, effectiveGrip * Mathf.Abs(lateralSpeed) * dt + 0.5f * dt);

            ForwardSpeed = forwardSpeed;
            rb.linearVelocity = forward * forwardSpeed + right * lateralSpeed + Vector3.up * velocity.y;
        }
    }
}
