using System.Collections;
using MarioKart.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MarioKart.UI
{
    /// <summary>
    /// Shown from GameState.Generating through the countdown. Just a status
    /// indicator -- no logic, no polling of the backend request itself
    /// (that's WorldRecipeClient's job). While Generating it mirrors
    /// GameManager.GenerationStatus (which includes mesh-loading progress
    /// when the game is configured to wait for meshes) over a dimmed
    /// backdrop; during Countdown the backdrop clears and it counts down
    /// GameConfig.countdownSeconds. RaceBootstrap owns the actual transition
    /// to Racing on the same timer.
    /// </summary>
    public class GenerationUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text statusText;
        [Range(0f, 1f)] public float generatingBackdropAlpha = 0.75f;

        private Image backdrop; // optional: the panel's own Image, if the scene gave it one
        private Coroutine countdown;

        private void Awake()
        {
            backdrop = panel != null ? panel.GetComponent<Image>() : null;
        }

        private void OnEnable()
        {
            GameManager.Instance.OnStateChanged += HandleStateChanged;
            GameManager.Instance.OnGenerationStatusChanged += HandleGenerationStatus;
            HandleStateChanged(GameManager.Instance.CurrentState);
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStateChanged -= HandleStateChanged;
                GameManager.Instance.OnGenerationStatusChanged -= HandleGenerationStatus;
            }
        }

        private void HandleGenerationStatus(string status)
        {
            if (statusText != null && GameManager.Instance.CurrentState == GameState.Generating)
            {
                statusText.text = status;
            }
        }

        private void SetBackdrop(float alpha)
        {
            if (backdrop == null) return;
            var color = backdrop.color;
            color.a = alpha;
            backdrop.color = color;
        }

        private void HandleStateChanged(GameState state)
        {
            bool visible = state == GameState.Generating || state == GameState.WorldReady || state == GameState.Countdown;
            panel.SetActive(visible);
            SetBackdrop(state == GameState.Generating ? generatingBackdropAlpha : 0f);

            if (countdown != null)
            {
                StopCoroutine(countdown);
                countdown = null;
            }

            if (statusText == null) return;

            switch (state)
            {
                case GameState.Generating:
                    statusText.text = string.IsNullOrEmpty(GameManager.Instance.GenerationStatus)
                        ? "Generating your world..."
                        : GameManager.Instance.GenerationStatus;
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
