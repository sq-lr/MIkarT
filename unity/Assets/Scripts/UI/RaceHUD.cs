using MarioKart.Core;
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
                player1Text.text = $"P1 Lap {player1Laps.CurrentLap}";
            }
            if (player2Laps != null && player2Text != null)
            {
                player2Text.text = $"P2 Lap {player2Laps.CurrentLap}";
            }
        }
    }
}
