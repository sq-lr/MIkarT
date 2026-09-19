using MarioKart.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MarioKart.UI
{
    /// <summary>
    /// Shown on GameState.Boot. Purely reactive to GameManager's state --
    /// never mutates game state on its own.
    /// </summary>
    public class LobbyUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Button startButton;

        private void Awake()
        {
            startButton.onClick.AddListener(OnStartClicked);
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
            panel.SetActive(state == GameState.Boot);
        }

        private void OnStartClicked()
        {
            GameManager.Instance.TransitionTo(GameState.Input);
        }
    }
}
