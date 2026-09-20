using MarioKart.Core;
using MarioKart.Players;
using MarioKart.Racing;
using UnityEngine;
using UnityEngine.UI;

namespace MarioKart.UI
{
    /// <summary>
    /// Shown on GameState.Racing. One lap/timer readout per player, each
    /// anchored to that player's half of the split screen.
    /// </summary>
    public class RaceHUD : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private LapManager player1Laps;
        [SerializeField] private LapManager player2Laps;
        [SerializeField] private Text player1Text;
        [SerializeField] private Text player2Text;

        private void OnEnable()
        {
            GameManager.Instance.OnStateChanged += HandleStateChanged;
            HandleStateChanged(GameManager.Instance.CurrentState);
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStateChanged -= HandleStateChanged;
            }
        }

        private void HandleStateChanged(GameState state)
        {
            panel.SetActive(state == GameState.Racing);
        }

        private void Update()
        {
            if (!panel.activeSelf) return;

            if (player1Laps != null && player1Text != null)
            {
                player1Text.text = FormatHud(player1Laps);
            }
            if (player2Laps != null && player2Text != null)
            {
                player2Text.text = FormatHud(player2Laps);
            }
        }

        private static string FormatHud(LapManager laps)
        {
            var kart = laps.GetComponent<KartController>();
            float kmh = kart != null ? Mathf.Abs(kart.ForwardSpeed) * 3.6f : 0f;
            return $"P{laps.playerIndex}   LAP {laps.CurrentLap}/{laps.TotalLaps}   {kmh:0}";
        }
    }
}
