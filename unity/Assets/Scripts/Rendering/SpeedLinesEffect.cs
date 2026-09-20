using MarioKart.CameraSystem;
using MarioKart.Players;
using UnityEngine;
using UnityEngine.UI;

namespace MarioKart.Rendering
{
    /// <summary>
    /// Comic-book speed lines: inked dash marks that shoot in from the edges
    /// of one player's half of the screen as their kart nears top speed.
    /// Intensity ramps from 0 at `activationFraction × maxSpeed` to full at
    /// maxSpeed, so easing off or scraping a wall (which caps speed) makes
    /// them vanish. The marks re-randomise their angle, length and which of
    /// them are drawn every `flickerInterval` seconds -- the hand-drawn
    /// flicker of a panel-to-panel motion effect rather than a smooth blur.
    ///
    /// Built entirely in code: one Screen Space - Overlay canvas per camera
    /// whose panel is anchored to that camera's viewport rect, holding
    /// `streakCount` Images of a procedurally drawn white streak with a
    /// black ink border (readable over both sky and ground). Overlay rather
    /// than Screen Space - Camera so the other player's camera can never
    /// see this camera's marks.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class SpeedLinesEffect : MonoBehaviour
    {
        [Tooltip("Fraction of maxSpeed at which the lines start to appear.")]
        [Range(0f, 1f)] public float activationFraction = 0.9f;
        [Tooltip("Number of dash marks around the edge of the viewport.")]
        [Range(4, 64)] public int streakCount = 28;
        [Tooltip("Seconds between re-randomising the marks.")]
        public float flickerInterval = 0.07f;
        [Tooltip("Fraction of the marks drawn in any one flicker frame.")]
        [Range(0f, 1f)] public float density = 0.7f;
        [Tooltip("Mark length as a fraction of the viewport height, at full speed.")]
        public Vector2 lengthRange = new Vector2(0.14f, 0.36f);
        [Tooltip("Mark thickness in pixels at 540p (scales with viewport height).")]
        public Vector2 thicknessRange = new Vector2(9f, 20f);
        [Tooltip("Random angular wobble around each mark's home angle, degrees.")]
        public float angleJitter = 5f;
        [Tooltip("How quickly the effect fades in/out with speed. Higher = snappier.")]
        public float response = 10f;

        private const float ReferenceHeight = 540f; // one half of a 1080p screen
        private const int TextureLength = 256;
        private const int TextureThickness = 32;

        // Shared: the streak is the same for both cameras.
        private static Sprite streakSprite;
        private static Texture2D streakTexture;
        private static int spriteUsers;

        private Camera cam;
        private PlayerCamera playerCamera;
        private Transform hookedTarget;
        private KartController kart;

        private Canvas canvas;
        private RectTransform panel;
        private Image[] streaks;
        private float[] homeAngles;
        private System.Random rng; // cosmetic only, seeded so the flicker is repeatable per camera

        private float intensity;
        private float nextFlickerAt;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            playerCamera = GetComponent<PlayerCamera>();
            rng = new System.Random(GetInstanceID());

            if (streakSprite == null)
            {
                streakTexture = BuildStreakTexture();
                streakSprite = Sprite.Create(streakTexture,
                    new Rect(0f, 0f, TextureLength, TextureThickness),
                    new Vector2(0f, 0.5f), // pivot at the thick end
                    pixelsPerUnit: 100f);
            }
            spriteUsers++;

            BuildCanvas();
        }

        private void OnDestroy()
        {
            if (canvas != null) Destroy(canvas.gameObject);
            if (--spriteUsers <= 0)
            {
                if (streakSprite != null) Destroy(streakSprite);
                if (streakTexture != null) Destroy(streakTexture);
                streakSprite = null;
                streakTexture = null;
                spriteUsers = 0;
            }
        }

        private void Update()
        {
            RefreshKart();

            float raw = 0f;
            if (kart != null)
            {
                float start = kart.maxSpeed * activationFraction;
                raw = Mathf.InverseLerp(start, kart.maxSpeed, kart.ForwardSpeed);
            }
            intensity = Mathf.Lerp(intensity, raw, 1f - Mathf.Exp(-response * Time.deltaTime));

            bool visible = intensity > 0.02f;
            if (panel.gameObject.activeSelf != visible) panel.gameObject.SetActive(visible);
            if (!visible) return;

            // Keep the panel glued to this camera's share of the screen.
            Rect view = cam.rect;
            panel.anchorMin = view.min;
            panel.anchorMax = view.max;

            if (Time.time >= nextFlickerAt)
            {
                nextFlickerAt = Time.time + flickerInterval;
                Relayout();
            }

            var tint = new Color(1f, 1f, 1f, intensity);
            foreach (var streak in streaks) streak.color = tint;
        }

        private void RefreshKart()
        {
            Transform target = playerCamera != null ? playerCamera.Target : null;
            if (target == hookedTarget) return;
            hookedTarget = target;
            kart = target != null ? target.GetComponentInParent<KartController>() : null;
        }

        /// <summary>
        /// Place every mark just outside the viewport edge on its (jittered)
        /// home angle, pointing at the centre, with a fresh random length and
        /// thickness; hide a random subset for the flicker.
        /// </summary>
        private void Relayout()
        {
            Vector2 size = panel.rect.size;
            if (size.x <= 0f || size.y <= 0f) return; // layout not ready yet this frame

            float unit = size.y / ReferenceHeight;
            float lengthScale = Mathf.Lerp(0.6f, 1f, intensity);

            for (int i = 0; i < streaks.Length; i++)
            {
                var streak = streaks[i];
                if (rng.NextDouble() > density)
                {
                    streak.enabled = false;
                    continue;
                }
                streak.enabled = true;

                float angle = homeAngles[i] + Range(-angleJitter, angleJitter);
                float rad = angle * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

                // Distance from the centre to the viewport edge along `dir`,
                // plus a little so the thick end starts off-screen.
                float edge = Mathf.Min(
                    size.x * 0.5f / Mathf.Max(Mathf.Abs(dir.x), 1e-4f),
                    size.y * 0.5f / Mathf.Max(Mathf.Abs(dir.y), 1e-4f));
                float length = Range(lengthRange.x, lengthRange.y) * size.y * lengthScale;
                float thickness = Range(thicknessRange.x, thicknessRange.y) * unit;

                var rect = streak.rectTransform;
                rect.sizeDelta = new Vector2(length, thickness);
                rect.anchoredPosition = dir * (edge + 4f * unit);
                rect.localRotation = Quaternion.Euler(0f, 0f, angle + 180f); // thick end at the edge, tip toward the centre
            }
        }

        private float Range(float min, float max) => Mathf.Lerp(min, max, (float)rng.NextDouble());

        // ------------------------------------------------------------------
        // Construction
        // ------------------------------------------------------------------

        private void BuildCanvas()
        {
            // A root object: an overlay canvas ignores its parent transform
            // anyway, and keeping it out from under the camera avoids any
            // doubt about that. Destroyed with this component.
            var canvasGo = new GameObject($"SpeedLines ({name})", typeof(Canvas), typeof(CanvasScaler));
            canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -1; // under the HUD / menu canvas
            canvasGo.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            // Anchored to the camera's viewport rect every frame in Update.
            var panelGo = new GameObject("Viewport", typeof(RectTransform));
            panelGo.transform.SetParent(canvasGo.transform, false);
            panel = panelGo.GetComponent<RectTransform>();
            panel.anchorMin = cam.rect.min;
            panel.anchorMax = cam.rect.max;
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;
            panel.pivot = new Vector2(0.5f, 0.5f);

            streaks = new Image[streakCount];
            homeAngles = new float[streakCount];
            float step = 360f / streakCount;
            for (int i = 0; i < streakCount; i++)
            {
                var go = new GameObject($"Streak_{i}", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(panel, false);
                var image = go.GetComponent<Image>();
                image.sprite = streakSprite;
                image.type = Image.Type.Simple;
                image.raycastTarget = false;
                image.enabled = false;

                var rect = image.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);

                streaks[i] = image;
                homeAngles[i] = i * step + step * 0.5f;
            }

            panel.gameObject.SetActive(false);
        }

        /// <summary>
        /// A white streak that tapers from a thick left end to a point on
        /// the right, wrapped in a black ink border. Hard edges only: it
        /// should read as a pen stroke, not a motion blur.
        /// </summary>
        private static Texture2D BuildStreakTexture()
        {
            var tex = new Texture2D(TextureLength, TextureThickness, TextureFormat.RGBA32, false)
            {
                name = "SpeedLineStreak",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            const float border = 3f;
            float centre = TextureThickness * 0.5f;
            float maxHalf = centre - 1f;
            var pixels = new Color32[TextureLength * TextureThickness];
            var ink = new Color32(20, 16, 24, 255);
            var core = new Color32(255, 255, 255, 255);
            var clear = new Color32(0, 0, 0, 0);

            for (int x = 0; x < TextureLength; x++)
            {
                float t = x / (float)(TextureLength - 1);
                // Round the thick end off a little so it does not look sawn off.
                float cap = Mathf.Clamp01(x / 6f);
                float half = maxHalf * Mathf.Pow(1f - t, 0.75f) * Mathf.Sqrt(cap);
                for (int y = 0; y < TextureThickness; y++)
                {
                    float d = Mathf.Abs(y + 0.5f - centre);
                    Color32 c = clear;
                    if (d < half - border) c = core;
                    else if (d < half) c = ink;
                    pixels[y * TextureLength + x] = c;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
