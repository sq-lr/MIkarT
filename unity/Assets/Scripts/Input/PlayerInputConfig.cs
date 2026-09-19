using System;
using UnityEngine;

namespace MarioKart.InputSystem
{
    /// <summary>
    /// Keyboard bindings for one local player. Plain serializable class
    /// (not a ScriptableObject) so no .asset file is required -- set via
    /// WASD()/ArrowKeys() or tuned directly in the Inspector.
    /// </summary>
    [Serializable]
    public class PlayerInputConfig
    {
        public KeyCode accelerate;
        public KeyCode brake;
        public KeyCode left;
        public KeyCode right;

        public static PlayerInputConfig WASD() => new PlayerInputConfig
        {
            accelerate = KeyCode.W,
            brake = KeyCode.S,
            left = KeyCode.A,
            right = KeyCode.D,
        };

        public static PlayerInputConfig ArrowKeys() => new PlayerInputConfig
        {
            accelerate = KeyCode.UpArrow,
            brake = KeyCode.DownArrow,
            left = KeyCode.LeftArrow,
            right = KeyCode.RightArrow,
        };
    }
}
