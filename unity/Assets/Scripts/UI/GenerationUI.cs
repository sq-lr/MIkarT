using System.Collections;
using MarioKart.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MarioKart.UI
{
    /// <summary>
    /// Shown from GameState.Generating through the countdown. Just a status
    /// indicator -- no logic, no polling of the backend request itself
    /// (that's WorldRecipeClient's job). During Countdown it counts down
    /// GameConfig.countdownSeconds on screen; RaceBootstrap owns the actual
    /// transition to Racing on the same timer.
    /// </summary>
    public class GenerationUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text statusText;

        private Coroutine countdown;

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
            bool visible = state == GameState.Generating || state == GameState.WorldReady || state == GameState.Countdown;
            panel.SetActive(visible);

            if (countdown != null)
            {
                StopCoroutine(countdown);
                countdown = null;
            }

            if (statusText == null) return;

            switch (state)
            {
                case GameState.Generating:
                    statusText.text = "Generating your world...";
                    break;
                case GameState.WorldReady:
                    statusText.text = "Get ready!";
                    break;
                case GameState.Countdown:
                    countdown = StartCoroutine(ShowCountdown());
                    break;
            }
        }

        private IEnumerator ShowCountdown()
        {
            int remaining = Mathf.CeilToInt(GameManager.Instance.Config.countdownSeconds);
            while (remaining > 0)
            {
                statusText.text = remaining.ToString();
                yield return new WaitForSeconds(1f);
                remaining--;
            }
            statusText.text = "GO!";
        }
    }
}
