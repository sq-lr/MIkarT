using System.Text;
using MarioKart.Core;
using MarioKart.Racing;
using UnityEngine;
using UnityEngine.UI;

namespace MarioKart.UI
{
    /// <summary>
    /// Shown on GameState.Results. Displays final placements and offers a
    /// "Play Again" reset back to Boot.
    /// </summary>
    public class ResultsUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text resultsText;
        [SerializeField] private Button playAgainButton;

        private void Awake()
        {
            playAgainButton.onClick.AddListener(OnPlayAgainClicked);
        }

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
            panel.SetActive(state == GameState.Results);
        }

        public void DisplayResult(RaceResult result)
        {
            var sb = new StringBuilder();
            foreach (var placement in result.placements)
            {
                sb.AppendLine($"#{placement.placement}  Player {placement.playerIndex}  {placement.finishTime:F2}s");
            }
            resultsText.text = sb.ToString();
        }

        private void OnPlayAgainClicked()
        {
            GameManager.Instance.TransitionTo(GameState.Boot);
        }
    }
}
