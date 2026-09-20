using UnityEngine;

namespace MarioKart.UI
{
    /// <summary>
    /// The two players' HUD colours (P1 red, P2 blue) and the shared gold
    /// used for wins and boosts, so RaceHUD and ResultsUI agree.
    /// </summary>
    public static class PlayerColors
    {
        public static readonly Color P1 = new Color(0.95f, 0.22f, 0.22f);
        public static readonly Color P2 = new Color(0.22f, 0.48f, 0.98f);
        public static readonly Color Gold = new Color(1f, 0.84f, 0.12f);

        public static Color For(int playerIndex) => playerIndex == 1 ? P1 : P2;
    }
}
