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
    /// GameConfig.countdownSeconds as big comic numerals -- each one pops
    /// in with a beep over a spinning starburst -- and "GO!" with an air
    /// horn on the moment RaceBootstrap flips the state to Racing (it owns
    /// that transition on the same timer). The panel lingers for the GO!
    /// pop, then hides.
    /// </summary>
    public class GenerationUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text statusText;
        [Range(0f, 1f)] public float generatingBackdropAlpha = 0.75f;

        [Header("Countdown look")]
        public int digitFontSize = 300;
        public int goFontSize = 360;
        [Tooltip("Colour of each numeral, last first (1, 2, 3, ...). Extra digits reuse the last entry.")]
        public Color[] digitColors = { new Color(1f, 0.3f, 0.25f), new Color(1f, 0.6f, 0.15f), new Color(1f, 0.9f, 0.25f) };
        public Color goColor = new Color(0.45f, 1f, 0.35f);
        public Color burstColor = new Color(1f, 1f, 1f, 0.9f);
        [Tooltip("Seconds for a numeral to pop in (scale overshoots, then settles).")]
        public float popDuration = 0.3f;
        [Tooltip("How far past full size the pop overshoots.")]
        public float popOvershoot = 1.35f;
        [Tooltip("Seconds GO! stays on screen after the race starts.")]
        public float goHold = 0.9f;

        [Header("Countdown sound")]
        [Range(0f, 1f)] public float beepVolume = 0.8f;
        [Range(0f, 1f)] public float goVolume = 0.9f;
        [Tooltip("Beep pitch on the first and last numeral; the ticks climb between them.")]
        public float firstBeepPitch = 0.9f;
        public float lastBeepPitch = 1.15f;

        private const string BeepPath = "Audio/countdown_beep";
        private const string GoPath = "Audio/countdown_go";

        private Image backdrop; // optional: the panel's own Image, if the scene gave it one
        private Coroutine countdown;
        private RectTransform burst;
        private Image burstImage;
        private Outline outline;
        private int normalFontSize;
        private Color normalColor;
        private Vector2 normalOutline;
        private GameState previousState;

        private void Awake()
        {
            backdrop = panel != null ? panel.GetComponent<Image>() : null;
            if (statusText != null)
            {
                normalFontSize = statusText.fontSize;
                normalColor = statusText.color;
                outline = statusText.GetComponent<Outline>();
                if (outline != null) normalOutline = outline.effectDistance;
                burst = CreateBurst(statusText.transform.parent, statusText.transform.GetSiblingIndex());
            }
        }

        private void OnDestroy()
        {
            if (burstImage != null && burstImage.sprite != null)
            {
                Destroy(burstImage.sprite.texture);
                Destroy(burstImage.sprite);
            }
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
            bool goMoment = state == GameState.Racing && previousState == GameState.Countdown;
            previousState = state;

            bool visible = state == GameState.Generating || state == GameState.WorldReady || state == GameState.Countdown || goMoment;
            panel.SetActive(visible);
            SetBackdrop(state == GameState.Generating ? generatingBackdropAlpha : 0f);

            if (countdown != null)
            {
                StopCoroutine(countdown);
                countdown = null;
            }
            ResetLook();

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
                case GameState.Racing:
                    if (goMoment) countdown = StartCoroutine(ShowGo());
                    break;
            }
        }

        private IEnumerator ShowCountdown()
        {
            int total = Mathf.CeilToInt(GameManager.Instance.Config.countdownSeconds);
            for (int remaining = total; remaining > 0; remaining--)
            {
                float tick = total > 1 ? (float)(total - remaining) / (total - 1) : 1f;
                UISounds.Play(BeepPath, beepVolume, Mathf.Lerp(firstBeepPitch, lastBeepPitch, tick));

                Color color = digitColors.Length > 0 ? digitColors[Mathf.Min(remaining - 1, digitColors.Length - 1)] : Color.white;
                // Numerals lean alternately left and right, like hand-lettered panels.
                float lean = (remaining % 2 == 0) ? 8f : -8f;
                yield return Pop(remaining.ToString(), digitFontSize, color, lean, spin: 25f, hold: 1f);
            }
        }

        private IEnumerator ShowGo()
        {
            UISounds.Play(GoPath, goVolume);
            yield return Pop("GO!", goFontSize, goColor, lean: -6f, spin: 140f, hold: goHold);
            panel.SetActive(false);
            countdown = null;
        }

        /// <summary>
        /// One comic beat: the text slams in from small with an overshoot,
        /// settling into a slight lean, while the starburst behind it pops
        /// with it and keeps turning. After the pop, both shrink a little
        /// over the hold so the next number reads as a fresh hit.
        /// </summary>
        private IEnumerator Pop(string content, int fontSize, Color color, float lean, float spin, float hold)
        {
            statusText.text = content;
            statusText.fontSize = fontSize;
            statusText.color = color;
            if (outline != null) outline.effectDistance = new Vector2(8f, -8f);
            if (burst != null)
            {
                burst.gameObject.SetActive(true);
                burstImage.color = burstColor;
                burst.sizeDelta = Vector2.one * fontSize * 2.4f;
            }

            var rect = statusText.rectTransform;
            float elapsed = 0f;
            while (elapsed < hold)
            {
                float k = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, popDuration));
                float settle = Mathf.Clamp01((elapsed - popDuration) / Mathf.Max(0.01f, hold - popDuration));
                // Overshoot past full size, spring back, then ease down a touch.
                float scale = Mathf.LerpUnclamped(0.15f, 1f, EaseOutBack(k, popOvershoot)) * Mathf.Lerp(1f, 0.88f, settle);
                float tilt = Mathf.Lerp(lean * 2.5f, lean, EaseOutBack(k, 1.2f));

                rect.localScale = Vector3.one * scale;
                rect.localRotation = Quaternion.Euler(0f, 0f, tilt);
                if (burst != null)
                {
                    burst.localScale = Vector3.one * scale;
                    burst.Rotate(0f, 0f, spin * Time.deltaTime);
                }

                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        private void ResetLook()
        {
            if (statusText == null) return;
            statusText.fontSize = normalFontSize;
            statusText.color = normalColor;
            statusText.rectTransform.localScale = Vector3.one;
            statusText.rectTransform.localRotation = Quaternion.identity;
            if (outline != null) outline.effectDistance = normalOutline;
            if (burst != null) burst.gameObject.SetActive(false);
        }

        /// <summary>Back-easing: shoots past 1 by an amount set by <paramref name="overshoot"/> and springs back.</summary>
        private static float EaseOutBack(float t, float overshoot)
        {
            float c1 = (overshoot - 1f) * 3.5f + 1.7f;
            float c3 = c1 + 1f;
            t -= 1f;
            return 1f + c3 * t * t * t + c1 * t * t;
        }

        /// <summary>
        /// A flat comic starburst Image behind the status text (inserted
        /// just before it so the text draws on top), hidden until the
        /// countdown. Built in code like the particle shapes in
        /// KartParticles, so the scene needs no sprite asset.
        /// </summary>
        private RectTransform CreateBurst(Transform parent, int siblingIndex)
        {
            var go = new GameObject("CountdownBurst", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.transform.SetSiblingIndex(siblingIndex);
            burstImage = go.GetComponent<Image>();
            burstImage.raycastTarget = false;
            var texture = BurstTexture(256, points: 14, innerRadius: 0.62f);
            burstImage.sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            burstImage.color = burstColor;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.one * 600f;
            go.SetActive(false);
            return rect;
        }

        /// <summary>Hard-edged spiky star polygon with a dark rim, alpha elsewhere.</summary>
        private static Texture2D BurstTexture(int size, int points, float innerRadius)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            var fill = new Color32(255, 255, 255, 255);
            var rim = new Color32(20, 20, 30, 255);
            var clear = new Color32(0, 0, 0, 0);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = Mathf.Atan2(dy, dx);
                    // Edge radius swings between the tips and the notches.
                    float wave = 0.5f + 0.5f * Mathf.Cos(angle * points);
                    float edge = Mathf.Lerp(innerRadius, 0.98f, Mathf.Pow(wave, 1.6f));
                    pixels[y * size + x] = r > edge ? clear : r > edge - 0.045f ? rim : fill;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
