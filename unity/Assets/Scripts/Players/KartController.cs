using MarioKart.InputSystem;
using MarioKart.World;
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

        [Header("Ground")]
        [Tooltip("How far below the kart's centre to look for the road, metres. Beyond this the kart counts as airborne.")]
        public float groundProbeDistance = 1.2f;
        [Tooltip("How quickly the kart tilts to follow a slope. Higher = snappier.")]
        public float groundAlignSpeed = 12f;

        [Header("Spin control")]
        [Tooltip("How fast physics-induced spin (from walls / the other kart) is bled off, per second. Higher = stops sooner.")]
        public float spinDamping = 12f;
        [Tooltip("Cap on collision-induced spin, in deg/s. Lower = spins less.")]
        public float maxCollisionSpin = 60f;

        private KartInput currentInput;
        private PhysicsMaterial frictionless;
        private float scrapingUntil; // Time.time until which the scrape cap applies
        private float skidUntil;     // Time.time until which grip is reduced
        private float boostUntil;
        private float boostMultiplier = 1f;
        private float paralyzeUntil;
        private float spinRemainingDeg;
        private float spinRateDeg;
        private float spinSign = 1f;

        public bool IsSkidding => Time.time < skidUntil;
        public bool IsBoosting => Time.time < boostUntil;
        public bool IsParalyzed => Time.time < paralyzeUntil;
        public bool IsSpinning => spinRemainingDeg > 0f;

        public float ForwardSpeed { get; private set; }

        /// <summary>Smoothed normal of the surface under the kart (world up when airborne).</summary>
        public Vector3 GroundNormal { get; private set; } = Vector3.up;

        /// <summary>False while the probe below the kart finds nothing (over a crest, off a drop).</summary>
        public bool IsGrounded { get; private set; } = true;

        private readonly RaycastHit[] groundHits = new RaycastHit[8];

        /// <summary>Current steering input in [-1, 1] (read by KartVisual to turn the front wheels).</summary>
        public float Steering => currentInput.steering;

        /// <summary>Current throttle input in [0, 1] (read by KartAudio to rev the engine).</summary>
        public float Throttle => currentInput.throttle;

        /// <summary>Current brake / reverse input in [0, 1] (read by KartAudio to drop the revs).</summary>
        public float Brake => currentInput.brake;

        /// <summary>
        /// Raised on first contact with anything solid -- a barrier wall or
        /// the other kart -- with the impact speed in m/s (the kart's own
        /// speed against a wall, the closing speed against another kart).
        /// PlayerCamera uses it to shake, KartAudio to play a hit.
        /// </summary>
        public event System.Action<float> Impact;

        /// <summary>Raised when a boost pickup takes effect. KartAudio plays the power-up jingle.</summary>
        public event System.Action PowerUp;

        /// <summary>Raised when a paralyze or spin pickup takes effect. KartAudio plays the power-down sound.</summary>
        public event System.Action PowerDown;

        /// <summary>Raised whenever any TrackObstacle takes effect, with which kind -- unlike PowerUp/PowerDown, distinguishes Paralyze from Spin. RaceHUD uses it to flash that player's screen border.</summary>
        public event System.Action<ObstacleKind> ObstacleHit;

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

            if (GetComponent<KartVisual>() == null)
            {
                gameObject.AddComponent<KartVisual>();
            }
            if (GetComponent<KartAudio>() == null)
            {
                gameObject.AddComponent<KartAudio>();
            }
            if (GetComponent<KartPickupEffect>() == null)
            {
                gameObject.AddComponent<KartPickupEffect>();
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

        /// <summary>
        /// Raise top speed for a while and kick current velocity toward the
        /// new cap. Called by TrackObstacle (boost pickup).
        /// </summary>
        public void ApplyBoost(float seconds, float multiplier)
        {
            if (rb == null || rb.isKinematic) return;
            boostUntil = Mathf.Max(boostUntil, Time.time + seconds);
            boostMultiplier = Mathf.Max(boostMultiplier, multiplier);

            Vector3 velocity = rb.linearVelocity;
            float forwardSpeed = Vector3.Dot(velocity, transform.forward);
            float target = maxSpeed * boostMultiplier;
            if (forwardSpeed < target)
            {
                float add = Mathf.Min(target - forwardSpeed, maxSpeed * 0.45f);
                rb.linearVelocity = velocity + transform.forward * add;
            }
            PowerUp?.Invoke();
            ObstacleHit?.Invoke(ObstacleKind.Boost);
        }

        /// <summary>Freeze horizontal motion and ignore input. Called by TrackObstacle.</summary>
        public void ApplyParalyze(float seconds)
        {
            if (rb == null || rb.isKinematic) return;
            paralyzeUntil = Time.time + seconds;
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            rb.angularVelocity = Vector3.zero;
            spinRemainingDeg = 0f;
            PowerDown?.Invoke();
            ObstacleHit?.Invoke(ObstacleKind.Paralyze);
        }

        /// <summary>
        /// Yaw the kart through <paramref name="turns"/> full rotations
        /// (sign = direction) while ignoring steering. Called by TrackObstacle.
        /// </summary>
        public void ApplySpin(float turns)
        {
            if (rb == null || rb.isKinematic) return;
            spinSign = turns < 0f ? -1f : 1f;
            spinRemainingDeg = Mathf.Abs(turns) * 360f;
            float duration = Mathf.Lerp(0.35f, 0.85f, Mathf.InverseLerp(0.25f, 1.25f, Mathf.Abs(turns)));
            spinRateDeg = spinRemainingDeg / Mathf.Max(0.2f, duration);
            PowerDown?.Invoke();
            ObstacleHit?.Invoke(ObstacleKind.Spin);
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

            if (IsParalyzed)
            {
                rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
                rb.angularVelocity = Vector3.zero;
                ForwardSpeed = 0f;
                return;
            }

            if (!IsBoosting) boostMultiplier = 1f;

            KartInput input = currentInput;
            if (IsSpinning)
            {
                float step = Mathf.Min(spinRemainingDeg, spinRateDeg * dt);
                spinRemainingDeg -= step;
                rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, spinSign * step, 0f));
                // Keep world-space velocity so the kart pirouettes instead of
                // steering into a circle. Grip re-aligns after the spin ends.
                ForwardSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);
                return;
            }

            Vector3 velocity = rb.linearVelocity;

            // ---- Ground: the road has hills, so the driving frame follows
            // the surface under the kart instead of the world's XZ plane ----
            ProbeGround(dt);
            Vector3 normal = GroundNormal;
            Vector3 heading = Vector3.ProjectOnPlane(rb.rotation * Vector3.forward, Vector3.up).normalized;
            if (heading.sqrMagnitude < 0.5f) heading = Vector3.forward; // pointing straight up/down: give up on this step's heading
            Vector3 forward = Vector3.ProjectOnPlane(heading, normal).normalized;
            Vector3 right = Vector3.Cross(normal, forward);

            float forwardSpeed = Vector3.Dot(velocity, forward);
            float lateralSpeed = Vector3.Dot(velocity, right);

            // ---- Longitudinal ----
            float boostedMax = maxSpeed * boostMultiplier;
            float topSpeed = Time.time < scrapingUntil ? boostedMax * barrierScrapeSpeedFactor : boostedMax;
            if (input.throttle > 0f)
            {
                forwardSpeed = Mathf.MoveTowards(forwardSpeed, topSpeed * input.throttle, acceleration * dt);
            }
            else if (input.brake > 0f)
            {
                // Brake to a stop, then reverse.
                float target = forwardSpeed > 0.05f ? 0f : -maxReverseSpeed * input.brake;
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
            float speedFactor = Mathf.Clamp(forwardSpeed / boostedMax, -1f, 1f);
            float yaw = input.steering * steerSpeed * speedFactor * dt;
            if (Mathf.Abs(yaw) > 0f)
            {
                heading = Quaternion.Euler(0f, yaw, 0f) * heading;
                forward = Vector3.ProjectOnPlane(heading, normal).normalized;
                right = Vector3.Cross(normal, forward);
            }
            // Heading about the world's up axis, body tilted to the ground:
            // on flat road this is exactly the old yaw-only rotation.
            rb.MoveRotation(Quaternion.LookRotation(forward, normal));

            // ---- Lateral grip: bleed off sideways slide ----
            float effectiveGrip = IsSkidding ? grip * skidGripFactor : grip;
            lateralSpeed = Mathf.MoveTowards(lateralSpeed, 0f, effectiveGrip * Mathf.Abs(lateralSpeed) * dt + 0.5f * dt);

            // Keep whatever gravity/contact put along the surface normal
            // (pressing into a slope, or falling when airborne).
            ForwardSpeed = forwardSpeed;
            rb.linearVelocity = forward * forwardSpeed + right * lateralSpeed + normal * Vector3.Dot(velocity, normal);
        }

        /// <summary>
        /// Look straight down from the kart's centre for the road (or the
        /// ground plane) and ease GroundNormal toward what it finds; toward
        /// world up when nothing is close enough (airborne over a crest).
        /// Other karts are ignored so driving over one doesn't tilt us.
        /// </summary>
        private void ProbeGround(float dt)
        {
            Vector3 target = Vector3.up;
            IsGrounded = false;

            int count = Physics.RaycastNonAlloc(rb.position, Vector3.down, groundHits, groundProbeDistance, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var hit = groundHits[i];
                if (hit.rigidbody != null) continue; // ourselves or the other kart
                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    target = hit.normal;
                    IsGrounded = true;
                }
            }

            GroundNormal = Vector3.Slerp(GroundNormal, target, 1f - Mathf.Exp(-groundAlignSpeed * dt));
        }
    }
}
