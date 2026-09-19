using MarioKart.InputSystem;
using UnityEngine;

namespace MarioKart.Players
{
    /// <summary>
    /// Placeholder arcade kart physics. Deliberately simple -- no drift,
    /// suspension, or collision tuning. Real vehicle feel is explicitly out
    /// of scope for this bootstrap.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class KartController : MonoBehaviour
    {
        [SerializeField] private Rigidbody rb;
        public float maxSpeed = 20f;
        public float acceleration = 10f;
        public float brakeForce = 15f;
        public float steerSpeed = 90f;

        private KartInput currentInput;

        private void Awake()
        {
            if (rb == null) rb = GetComponent<Rigidbody>();
        }

        public void ApplyInput(KartInput input)
        {
            currentInput = input;
        }

        private void FixedUpdate()
        {
            float forwardSpeed = Vector3.Dot(rb.velocity, transform.forward);
            float speedFactor = Mathf.Clamp01(1f - Mathf.Abs(forwardSpeed) / maxSpeed);

            if (currentInput.throttle > 0f)
            {
                rb.AddForce(transform.forward * (currentInput.throttle * acceleration), ForceMode.Acceleration);
            }
            if (currentInput.brake > 0f)
            {
                rb.AddForce(-transform.forward * (currentInput.brake * brakeForce), ForceMode.Acceleration);
            }

            float steerAmount = currentInput.steering * steerSpeed * Time.fixedDeltaTime * speedFactor;
            transform.Rotate(Vector3.up, steerAmount);
        }
    }
}
