using MarioKart.Rendering;
using UnityEngine;

namespace MarioKart.CameraSystem
{
    /// <summary>
    /// Follows one kart and renders into its half of the split screen.
    /// Both cameras render the exact same generated world; no per-player
    /// scene duplication.
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

        /// <summary>Where the camera wants to be for the target's current pose.</summary>
        public Vector3 DesiredPosition => target.position + target.rotation * offset;

        /// <summary>Kart yaw (+ yawDegrees) and the configured pitch; roll is always zero.</summary>
        public Quaternion DesiredRotation => Quaternion.Euler(pitchDegrees, target.eulerAngles.y + yawDegrees, 0f);

        /// <summary>Jump straight to the desired pose (no smoothing), e.g. at the start line.</summary>
        public void SnapToTarget()
        {
            if (target == null) return;
            transform.SetPositionAndRotation(DesiredPosition, DesiredRotation);
        }

        private void Awake()
        {
            if (cam == null) cam = GetComponent<Camera>();

            // The scene builder adds the paper-grain post effect; ensure it
            // here too so a scene built before it existed still gets it.
            if (cam != null && cam.GetComponent<PaperGrainEffect>() == null)
            {
                cam.gameObject.AddComponent<PaperGrainEffect>();
            }
        }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
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

            // Rotate the offset by the kart's rotation rather than TransformPoint:
            // the kart is a non-uniformly scaled cube, and TransformPoint would
            // scale the offset with it (0.6× up, 2.6× back).
            transform.position = Vector3.Lerp(transform.position, DesiredPosition, followLerp * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, DesiredRotation, followLerp * Time.deltaTime);
        }
    }
}
