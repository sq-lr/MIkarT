using MarioKart.Core;
using MarioKart.Players;
using MarioKart.Racing;
using MarioKart.World;
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

        [Header("Obstacle flash")]
        [Tooltip("Thickness in canvas units of the flashed border strip.")]
        public float flashThickness = 18f;
        [Tooltip("How fast the flash fades back to clear, in alpha per second.")]
        public float flashFadeSpeed = 2.5f;

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

        // A class, not a struct: TriggerFlash mutates it from an event
        // callback registered once at Build time, so it needs a stable
        // reference rather than a copy.
        private class BorderFlash
        {
            public Image top, bottom, left, right;
            public Color color = Color.white;
            public float alpha;
        }

        private SpeedBar p1Bar, p2Bar;
        private BorderFlash p1Flash, p2Flash;
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
            UpdateFlash(p1Flash);
            UpdateFlash(p2Flash);
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

            // Full-half border overlays, flashed by that player's own
            // TrackObstacle hits (green boost, yellow spin, red paralyze).
            p1Flash = BuildBorderFlash("P1Flash", new Vector2(0f, 0.5f), Vector2.one);
            p2Flash = BuildBorderFlash("P2Flash", Vector2.zero, new Vector2(1f, 0.5f));
            if (p1Bar.kart != null) p1Bar.kart.ObstacleHit += kind => TriggerFlash(p1Flash, kind);
            if (p2Bar.kart != null) p2Bar.kart.ObstacleHit += kind => TriggerFlash(p2Flash, kind);
        }

        private BorderFlash BuildBorderFlash(string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var root = CreateRect(name, panel.transform);
            root.anchorMin = anchorMin;
            root.anchorMax = anchorMax;
            root.offsetMin = root.offsetMax = Vector2.zero;

            var flash = new BorderFlash();

            flash.top = CreateImage("FlashTop", root, Color.clear);
            flash.top.rectTransform.anchorMin = new Vector2(0f, 1f);
            flash.top.rectTransform.anchorMax = Vector2.one;
            flash.top.rectTransform.pivot = new Vector2(0.5f, 1f);
            flash.top.rectTransform.offsetMin = Vector2.zero;
            flash.top.rectTransform.offsetMax = Vector2.zero;
            flash.top.rectTransform.sizeDelta = new Vector2(0f, flashThickness);
            flash.top.rectTransform.anchoredPosition = Vector2.zero;

            flash.bottom = CreateImage("FlashBottom", root, Color.clear);
            flash.bottom.rectTransform.anchorMin = Vector2.zero;
            flash.bottom.rectTransform.anchorMax = new Vector2(1f, 0f);
            flash.bottom.rectTransform.pivot = new Vector2(0.5f, 0f);
            flash.bottom.rectTransform.offsetMin = Vector2.zero;
            flash.bottom.rectTransform.offsetMax = Vector2.zero;
            flash.bottom.rectTransform.sizeDelta = new Vector2(0f, flashThickness);
            flash.bottom.rectTransform.anchoredPosition = Vector2.zero;

            flash.left = CreateImage("FlashLeft", root, Color.clear);
            flash.left.rectTransform.anchorMin = Vector2.zero;
            flash.left.rectTransform.anchorMax = new Vector2(0f, 1f);
            flash.left.rectTransform.pivot = new Vector2(0f, 0.5f);
            flash.left.rectTransform.offsetMin = Vector2.zero;
            flash.left.rectTransform.offsetMax = Vector2.zero;
            flash.left.rectTransform.sizeDelta = new Vector2(flashThickness, 0f);
            flash.left.rectTransform.anchoredPosition = Vector2.zero;

            flash.right = CreateImage("FlashRight", root, Color.clear);
            flash.right.rectTransform.anchorMin = new Vector2(1f, 0f);
            flash.right.rectTransform.anchorMax = Vector2.one;
            flash.right.rectTransform.pivot = new Vector2(1f, 0.5f);
            flash.right.rectTransform.offsetMin = Vector2.zero;
            flash.right.rectTransform.offsetMax = Vector2.zero;
            flash.right.rectTransform.sizeDelta = new Vector2(flashThickness, 0f);
            flash.right.rectTransform.anchoredPosition = Vector2.zero;

            return flash;
        }

        /// <summary>Green for a boost, yellow for a disorienting spin, red for the harsher paralyze stun.</summary>
        private static Color ColorForObstacle(ObstacleKind kind)
        {
            switch (kind)
            {
                case ObstacleKind.Boost: return new Color(0.25f, 0.95f, 0.35f);
                case ObstacleKind.Spin: return new Color(1f, 0.92f, 0.15f);
                case ObstacleKind.Paralyze: return new Color(0.98f, 0.18f, 0.15f);
                default: return Color.white;
            }
        }

        private static void TriggerFlash(BorderFlash flash, ObstacleKind kind)
        {
            flash.color = ColorForObstacle(kind);
            flash.alpha = 1f;
        }

        private void UpdateFlash(BorderFlash flash)
        {
            if (flash == null || flash.alpha <= 0f) return;
            flash.alpha = Mathf.Max(0f, flash.alpha - flashFadeSpeed * Time.deltaTime);
            Color c = flash.color;
            c.a = flash.alpha;
            flash.top.color = c;
            flash.bottom.color = c;
            flash.left.color = c;
            flash.right.color = c;
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
