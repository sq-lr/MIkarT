using UnityEngine;

namespace MarioKart.InputSystem
{
    /// <summary>
    /// Vendor-neutral input produced each frame for a kart. KartController
    /// only ever sees this struct, never raw KeyCodes -- swapping keyboard
    /// for a controller later means adding another PlayerInput
    /// implementation, not touching kart physics.
    /// </summary>
    public struct KartInput
    {
        public float throttle;
        public float steering;
        public float brake;
    }

    /// <summary>
    /// Reads the local keyboard for one player (1 = WASD, 2 = arrow keys)
    /// and produces a KartInput each frame.
    /// </summary>
    public class PlayerInput : MonoBehaviour
    {
        [SerializeField] private int playerIndex = 1;
        [SerializeField] private PlayerInputConfig config;

        private void Reset()
        {
            config = playerIndex == 2 ? PlayerInputConfig.ArrowKeys() : PlayerInputConfig.WASD();
        }

        public KartInput ReadInput()
        {
            return new KartInput
            {
                throttle = UnityEngine.Input.GetKey(config.accelerate) ? 1f : 0f,
                brake = UnityEngine.Input.GetKey(config.brake) ? 1f : 0f,
                steering = (UnityEngine.Input.GetKey(config.right) ? 1f : 0f) - (UnityEngine.Input.GetKey(config.left) ? 1f : 0f),
            };
        }
    }
}
