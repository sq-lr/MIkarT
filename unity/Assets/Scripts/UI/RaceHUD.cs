using MarioKart.Core;
using MarioKart.Players;
using MarioKart.Racing;
using UnityEngine;
using UnityEngine.UI;

namespace MarioKart.UI
{
    /// <summary>
    /// Shown on GameState.Racing. One lap readout per player, each anchored
    /// to the top-left of that player's half of the split screen, with a
    /// toon speed bar underneath: a black-outlined, tick-marked bar that
    /// fills with |speed| / maxSpeed and shifts colour as it does (green at
    /// a crawl through yellow to red flat out), pulses gold while boosting
    /// and greys out while paralyzed or spinning, with the
    /// km/h figure at its right end. The bars are built here at runtime
    /// (plain Images, like ResultsUI's stripe) so the scene needs nothing
    /// beyond the two lap Texts.
    /// </summary>
    public class RaceHUD : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private LapManager player1Laps;
        [SerializeField] private LapManager player2Laps;
        [SerializeField] private Text player1Text;
        [SerializeField] private Text player2Text;

        [Header("Speed bar")]
        [Tooltip("Bar size in canvas units (the canvas is scaled from 1920x1080).")]
        public Vector2 barSize = new Vector2(320f, 22f);
        [Tooltip("Offset of the bar's top-left corner from the top-left of the player's half; sits under the lap text.")]
        public Vector2 barOffset = new Vector2(24f, -78f);
        [Tooltip("How fast the shown fill chases the real speed, in bar-widths per second, so bumps don't make it flicker.")]
        public float fillSlew = 3f;
        [Tooltip("Number of segments the tick marks divide the bar into.")]
        public int segments = 8;
        [Tooltip("Fill colour by how full the bar is: 0 = stopped, 1 = top speed.")]
        public Gradient fillGradient = DefaultGradient();

        private static readonly Color TrackColor = new Color(0.08f, 0.08f, 0.1f, 0.9f);
        private static readonly Color StunColor = new Color(0.45f, 0.47f, 0.52f);

        private struct SpeedBar
        {
            public KartController kart;
            public RectTransform fill;
            public Image fillImage;
            public Text label;
            public float shown; // smoothed 0..1
        }

        private SpeedBar p1Bar, p2Bar;
        private bool built;

        private void Awake()
        {
            Build();
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

            UpdateBar(ref p1Bar);
            UpdateBar(ref p2Bar);
        }

        private static string FormatHud(LapManager laps)
        {
            return $"P{laps.playerIndex}   LAP {laps.CurrentLap}/{laps.TotalLaps}";
        }

        private void UpdateBar(ref SpeedBar bar)
        {
            if (bar.kart == null || bar.fill == null) return;

            float speed = Mathf.Abs(bar.kart.ForwardSpeed);
            float target = bar.kart.maxSpeed > 0f ? speed / bar.kart.maxSpeed : 0f;
            bar.shown = Mathf.MoveTowards(bar.shown, Mathf.Clamp01(target), fillSlew * Time.deltaTime);
            bar.fill.anchorMax = new Vector2(bar.shown, 1f);

            Color color;
            if (bar.kart.IsParalyzed || bar.kart.IsSpinning)
            {
                color = StunColor;
            }
            else if (bar.kart.IsBoosting || target > 1.02f)
            {
                // Pulse toward white so a boost reads at a glance.
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.PI * 2f * 6f);
                color = Color.Lerp(PlayerColors.Gold, Color.white, pulse * 0.5f);
            }
            else
            {
                color = fillGradient.Evaluate(bar.shown);
            }
            bar.fillImage.color = color;

            if (bar.label != null)
            {
                bar.label.text = $"{speed * 3.6f:0}";
            }
        }

        // ------------------------------------------------------------------

        /// <summary>Green at a crawl, yellow at half, red flat out.</summary>
        private static Gradient DefaultGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.25f, 0.9f, 0.35f), 0f),
                    new GradientColorKey(new Color(1f, 0.9f, 0.2f), 0.5f),
                    new GradientColorKey(new Color(1f, 0.55f, 0.1f), 0.78f),
                    new GradientColorKey(new Color(0.98f, 0.2f, 0.18f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }

        private void Build()
        {
            if (built || panel == null) return;
            built = true;

            // Same anchoring as the scene's P1Text / P2Text: top-left of the
            // top half for P1, top-left of the bottom half for P2.
            p1Bar = BuildBar("P1SpeedBar", new Vector2(0f, 1f), player1Laps);
            p2Bar = BuildBar("P2SpeedBar", new Vector2(0f, 0.5f), player2Laps);
        }

        private SpeedBar BuildBar(string name, Vector2 anchor, LapManager laps)
        {
            var bar = new SpeedBar { kart = laps != null ? laps.GetComponent<KartController>() : null };

            var root = CreateRect(name, panel.transform);
            root.anchorMin = root.anchorMax = anchor;
            root.pivot = new Vector2(0f, 1f);
            root.anchoredPosition = barOffset;
            root.sizeDelta = barSize;

            // Hard black outline: an Image a few units bigger, behind the rest.
            var outline = CreateImage("Outline", root, Color.black);
            Stretch(outline.rectTransform, -3f);

            var track = CreateImage("Track", root, TrackColor);
            Stretch(track.rectTransform, 0f);

            var fill = CreateImage("Fill", root, fillGradient.Evaluate(0f));
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            bar.fill = fill.rectTransform;
            bar.fillImage = fill;

            // Tick marks over the fill: a segmented speedo look.
            for (int i = 1; i < Mathf.Max(1, segments); i++)
            {
                var tick = CreateImage($"Tick{i}", root, new Color(0f, 0f, 0f, 0.5f));
                float x = i / (float)segments;
                tick.rectTransform.anchorMin = new Vector2(x, 0f);
                tick.rectTransform.anchorMax = new Vector2(x, 1f);
                tick.rectTransform.sizeDelta = new Vector2(2f, 0f);
                tick.rectTransform.anchoredPosition = Vector2.zero;
            }

            // km/h figure just past the bar's right end.
            var labelGo = new GameObject("Speed", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(root, false);
            var label = labelGo.GetComponent<Text>();
            label.font = GameFonts.Display;
            label.fontSize = 20;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            label.text = "0";
            var labelOutline = labelGo.AddComponent<Outline>();
            labelOutline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            labelOutline.effectDistance = new Vector2(2f, -2f);
            var labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(1f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0f, 0.5f);
            labelRect.anchoredPosition = new Vector2(10f, 0f);
            labelRect.sizeDelta = new Vector2(80f, 0f);
            bar.label = label;

            return bar;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Fill the parent, grown outward by `margin` on every side.</summary>
        private static void Stretch(RectTransform rect, float margin)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(margin, margin);
            rect.offsetMax = new Vector2(-margin, -margin);
        }
    }
}
