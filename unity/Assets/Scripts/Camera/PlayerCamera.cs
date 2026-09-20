using MarioKart.Players;
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
        [Tooltip("Camera position relative to the kart (local space): x right, y up, z back.")]
        public Vector3 offset = new Vector3(0f, 4.4f, -5.4f);
        [Tooltip("Point the camera aims at, relative to the kart. Ahead and slightly up keeps the horizon in view.")]
        public Vector3 lookOffset = new Vector3(0f, 1.15f, 4f);
        public float followLerp = 8f;
        [Tooltip("Base vertical field of view.")]
        public float baseFov = 52f;
        [Tooltip("Extra FOV at full speed, so going fast reads as going fast.")]
        public float speedFovBoost = 7f;

        private KartController kart;

        private static readonly Vector3 LegacyOffset = new Vector3(0f, 3.5f, -3.5f);
        private static readonly Vector3 LegacyLook = new Vector3(0f, 0f, 3f);

        private void Awake()
        {
            if (cam == null) cam = GetComponent<Camera>();
            // Already-wired scenes still have the old close chase cam on the
            // serialized component; lift it so the watercolor sky reads.
            if (offset == LegacyOffset && lookOffset == LegacyLook)
            {
                offset = new Vector3(0f, 4.4f, -5.4f);
                lookOffset = new Vector3(0f, 1.15f, 4f);
                baseFov = 52f;
                speedFovBoost = 7f;
            }
            if (GetComponent<GhibliPostEffect>() == null)
            {
                gameObject.AddComponent<GhibliPostEffect>();
            }
            CacheKart();
        }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            CacheKart();
        }

        private void CacheKart()
        {
            kart = target != null ? target.GetComponent<KartController>() : null;
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

            Vector3 desiredPosition = target.TransformPoint(offset);
            transform.position = Vector3.Lerp(transform.position, desiredPosition, followLerp * Time.deltaTime);

            Vector3 lookTarget = target.TransformPoint(lookOffset);
            Quaternion desiredRotation = Quaternion.LookRotation(lookTarget - transform.position, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, followLerp * Time.deltaTime);

            if (kart != null && cam != null)
            {
                float speed01 = Mathf.Clamp01(Mathf.Abs(kart.ForwardSpeed) / Mathf.Max(0.01f, kart.maxSpeed));
                float want = baseFov + speedFovBoost * speed01 * speed01;
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, want, followLerp * Time.deltaTime);
            }
        }
    }
}
