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
        public Vector3 offset = new Vector3(0f, 3.5f, -3.5f);
        [Tooltip("Point the camera aims at, relative to the kart. Ahead and slightly up keeps the horizon in view.")]
        public Vector3 lookOffset = new Vector3(0f, 0f, 3f);
        public float followLerp = 8f;

        private void Awake()
        {
            if (cam == null) cam = GetComponent<Camera>();
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

            Vector3 desiredPosition = target.TransformPoint(offset);
            transform.position = Vector3.Lerp(transform.position, desiredPosition, followLerp * Time.deltaTime);

            Vector3 lookTarget = target.TransformPoint(lookOffset);
            Quaternion desiredRotation = Quaternion.LookRotation(lookTarget - transform.position, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, followLerp * Time.deltaTime);
        }
    }
}
