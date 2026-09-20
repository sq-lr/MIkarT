using MarioKart.Players;
using MarioKart.Rendering;
using UnityEngine;

namespace MarioKart.CameraSystem
{
    /// <summary>
    /// Follows one kart and renders into its half of the split screen.
    /// Both cameras render the exact same generated world; no per-player
    /// scene duplication.
    ///
    /// Also owns the screen shake: a wall or kart hit on the followed kart adds
    /// "trauma" that decays over `shakeDuration`, and the frame is offset
    /// and rolled by trauma² × noise on top of the smoothed follow pose (so
    /// the shake never feeds back into the follow lerp).
    /// </summary>
    public class PlayerCamera : MonoBehaviour
    {
        [SerializeField] private Camera cam;
        [SerializeField] private Transform target;
        [Tooltip("Camera position relative to the kart, in metres (unaffected by the kart's scale): x right, y up, z back.")]
        public Vector3 offset = new Vector3(0f, 2.15f, -6.2f);
        [Tooltip("Downward tilt in degrees.")]
        public float pitchDegrees = 12.47f;
        [Tooltip("Extra yaw relative to the kart's heading, in degrees (0 = look straight along the kart).")]
        public float yawDegrees = 0f;
        public float followLerp = 8f;

        [Header("Impact shake")]
        [Tooltip("Seconds for a full-strength shake to die out.")]
        public float shakeDuration = 0.4f;
        [Tooltip("Sideways / vertical camera offset at full trauma, metres.")]
        public float shakeAmplitude = 0.22f;
        [Tooltip("Camera roll at full trauma, degrees.")]
        public float shakeRollDegrees = 2.5f;
        [Tooltip("How jittery the shake is (noise samples per second).")]
        public float shakeFrequency = 26f;
        [Tooltip("Trauma added by a wall or kart hit at the kart's top speed; a slow tap adds a third of this.")]
        [Range(0f, 1f)] public float impactTrauma = 0.7f;

        private KartController hookedKart;
        private Vector3 followPosition;
        private Quaternion followRotation;
        private float trauma;      // 0..1, decays linearly
        private float noiseOffset; // per-camera so the two halves don't shake in sync

        public Transform Target => target;

        /// <summary>Where the camera wants to be for the target's current pose.</summary>
        public Vector3 DesiredPosition => target.position + target.rotation * offset;

        /// <summary>
        /// Looks along the kart's forward (yaw *and* slope pitch, so the
        /// camera tips with the road over hills), plus the configured
        /// pitch/yaw offsets; roll is always zero.
        /// </summary>
        public Quaternion DesiredRotation
        {
            get
            {
                Vector3 forward = target.forward;
                if (Vector3.Cross(forward, Vector3.up).sqrMagnitude < 1e-4f) forward = Vector3.forward; // vertical: no defined yaw
                return Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(pitchDegrees, yawDegrees, 0f);
            }
        }

        /// <summary>Jump straight to the desired pose (no smoothing), e.g. at the start line.</summary>
        public void SnapToTarget()
        {
            if (target == null) return;
            followPosition = DesiredPosition;
            followRotation = DesiredRotation;
            trauma = 0f;
            transform.SetPositionAndRotation(followPosition, followRotation);
        }

        /// <summary>Add shake. `strength` is clamped to [0, 1] and stacks up to 1.</summary>
        public void Shake(float strength)
        {
            trauma = Mathf.Clamp01(trauma + Mathf.Clamp01(strength));
        }

        private void Awake()
        {
            if (cam == null) cam = GetComponent<Camera>();
            noiseOffset = GetInstanceID() * 0.137f;
            followPosition = transform.position;
            followRotation = transform.rotation;

            // The scene builder adds the post/overlay effects; ensure them
            // here too so a scene built before they existed still gets them.
            if (cam != null && cam.GetComponent<PaperGrainEffect>() == null)
            {
                cam.gameObject.AddComponent<PaperGrainEffect>();
            }
            if (cam != null && cam.GetComponent<SpeedLinesEffect>() == null)
            {
                cam.gameObject.AddComponent<SpeedLinesEffect>();
            }
        }

        private void OnEnable()
        {
            HookTarget();
        }

        private void OnDisable()
        {
            UnhookTarget();
        }

        public void SetTarget(Transform newTarget)
        {
            UnhookTarget();
            target = newTarget;
            if (isActiveAndEnabled) HookTarget();
        }

        /// <summary>
        /// playerIndex 1 renders the top half of the screen, 2 the bottom half.
        /// </summary>
        public void SetViewportForPlayer(int playerIndex)
        {
            cam.rect = playerIndex == 1
                ? new Rect(0f, 0.5f, 1f, 0.5f)
                : new Rect(0f, 0f, 1f, 0.5f);
        }

        private void LateUpdate()
        {
            if (target == null) return;

            float dt = Time.deltaTime;

            // Rotate the offset by the kart's rotation rather than TransformPoint:
            // the kart is a non-uniformly scaled cube, and TransformPoint would
            // scale the offset with it (0.6× up, 2.6× back).
            followPosition = Vector3.Lerp(followPosition, DesiredPosition, followLerp * dt);
            followRotation = Quaternion.Slerp(followRotation, DesiredRotation, followLerp * dt);

            if (trauma <= 0f)
            {
                transform.SetPositionAndRotation(followPosition, followRotation);
                return;
            }

            // Squaring trauma makes a big hit feel much bigger than a scrape
            // and lets the tail of the shake fade out gently.
            float shake = trauma * trauma;
            float t = Time.time * shakeFrequency + noiseOffset;
            float dx = (Mathf.PerlinNoise(t, 0.3f) * 2f - 1f) * shakeAmplitude * shake;
            float dy = (Mathf.PerlinNoise(0.7f, t) * 2f - 1f) * shakeAmplitude * shake;
            float roll = (Mathf.PerlinNoise(t, t) * 2f - 1f) * shakeRollDegrees * shake;

            transform.SetPositionAndRotation(
                followPosition + followRotation * new Vector3(dx, dy, 0f),
                followRotation * Quaternion.Euler(0f, 0f, roll));

            trauma = Mathf.Max(0f, trauma - dt / Mathf.Max(0.01f, shakeDuration));
        }

        private void HookTarget()
        {
            var kart = target != null ? target.GetComponentInParent<KartController>() : null;
            if (kart == hookedKart) return;
            UnhookTarget();
            hookedKart = kart;
            if (hookedKart != null) hookedKart.Impact += OnImpact;
        }

        private void UnhookTarget()
        {
            if (hookedKart != null) hookedKart.Impact -= OnImpact;
            hookedKart = null;
        }

        private void OnImpact(float impactSpeed)
        {
            float speedFraction = Mathf.Clamp01(impactSpeed / Mathf.Max(0.01f, hookedKart.maxSpeed));
            Shake(impactTrauma * Mathf.Lerp(0.33f, 1f, speedFraction));
        }
    }
}
