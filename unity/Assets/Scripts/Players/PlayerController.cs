using MarioKart.InputSystem;
using UnityEngine;

namespace MarioKart.Players
{
    /// <summary>
    /// Wires one local player's input into their kart. Kept intentionally
    /// thin -- all physics live in KartController.
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] private PlayerInput input;
        [SerializeField] private KartController kart;

        private void Update()
        {
            kart.ApplyInput(input.ReadInput());
        }
    }
}
