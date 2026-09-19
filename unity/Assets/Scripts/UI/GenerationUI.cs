using MarioKart.Core;
using UnityEngine;

namespace MarioKart.UI
{
    /// <summary>
    /// Shown on GameState.Generating. Just a status indicator -- no logic,
    /// no polling of the backend request itself (that's WorldRecipeClient's job).
    /// </summary>
    public class GenerationUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;

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
            panel.SetActive(state == GameState.Generating);
        }
    }
}
