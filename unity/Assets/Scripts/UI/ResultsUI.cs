using System.Collections;
using System.Collections.Generic;
using MarioKart.Core;
using MarioKart.Racing;
using UnityEngine;
using UnityEngine.UI;

namespace MarioKart.UI
{
    /// <summary>
    /// Full-width translucent chess-checkered stripe on each player's
    /// split-screen half. Winner gets WIN + confetti; loser gets DEFEAT
    /// and loss pop-ups. Karts keep driving.
    /// </summary>
    public class ResultsUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Button playAgainButton;

        private HalfView p1;
        private HalfView p2;
        private Button playAgainCopy;
        private bool built;
        private int shownWinner;
        private Coroutine showRoutine;
        private readonly List<Confetti> bits = new List<Confetti>();

        private struct HalfView
        {
            public RectTransform root;
            public RectTransform strip;
            public RectTransform confettiRoot;
            public RectTransform popRoot;
            public Text headline;
        }

        private struct Confetti
        {
            public RectTransform rect;
            public Vector2 velocity;
            public float spin;
            public float life;
        }

        private static readonly Color Gold = new Color(1f, 0.84f, 0.12f);
        private static readonly Color P1Red = new Color(0.95f, 0.22f, 0.22f);
        private static readonly Color P2Blue = new Color(0.22f, 0.48f, 0.98f);
        private static readonly Color DefeatInk = new Color(0.72f, 0.74f, 0.80f);
        private static readonly Color[] WinColors =
        {
            new Color(1f, 0.2f, 0.25f, 0.95f),
            new Color(0.2f, 0.55f, 1f, 0.95f),
            new Color(1f, 0.85f, 0.1f, 0.95f),
            new Color(0.2f, 0.9f, 0.4f, 0.95f),
            new Color(1f, 0.45f, 0.85f, 0.95f),
            Color.white,
            Color.black,
        };
        private static readonly Color[] DefeatColors =
        {
            new Color(0.35f, 0.36f, 0.40f, 0.85f),
            new Color(0.55f, 0.20f, 0.22f, 0.8f),
            new Color(0.15f, 0.16f, 0.20f, 0.9f),
            new Color(0.70f, 0.72f, 0.76f, 0.7f),
        };

        private void Awake()
        {
            Build();
            if (playAgainButton != null)
            {
                playAgainButton.onClick.AddListener(OnPlayAgainClicked);
                UISounds.Attach(playAgainButton);
            }
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
            if (state != GameState.Racing)
            {
                panel.SetActive(false);
                shownWinner = 0;
            }
        }

        public void DisplayResult(RaceResult result)
        {
            Build();
            if (result == null || result.placements == null || result.placements.Count == 0) return;

            var winner = result.placements[0];
            if (shownWinner == winner.playerIndex) return;

            shownWinner = winner.playerIndex;
            panel.SetActive(true);

            if (showRoutine != null) StopCoroutine(showRoutine);
            showRoutine = StartCoroutine(PlayFinish(winner.playerIndex));
        }

        private IEnumerator PlayFinish(int winnerIndex)
        {
            int loserIndex = winnerIndex == 1 ? 2 : 1;
            var winHalf = HalfFor(winnerIndex);
            var loseHalf = HalfFor(loserIndex);
            Color winColor = winnerIndex == 1 ? P1Red : P2Blue;
            Color loseColor = loserIndex == 1 ? P1Red : P2Blue;

            winHalf.headline.text = "WIN";
            winHalf.headline.color = Gold;
            loseHalf.headline.text = "DEFEAT";
            loseHalf.headline.color = DefeatInk;

            winHalf.strip.localScale = loseHalf.strip.localScale = new Vector3(0.12f, 1f, 1f);

            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime * 5.2f;
                float k = Mathf.Clamp01(t);
                float x = Mathf.LerpUnclamped(0.12f, 1f, EaseOutBack(k));
                winHalf.strip.localScale = loseHalf.strip.localScale = new Vector3(x, 1f, 1f);
                yield return null;
            }
            winHalf.strip.localScale = loseHalf.strip.localScale = Vector3.one;

            SpawnWinPopups(winHalf, winColor);
            SpawnDefeatPopups(loseHalf, loseColor);
            Burst(winHalf, 90, winColor, win: true);
            Burst(loseHalf, 40, loseColor, win: false);

            float elapsed = 0f;
            while (panel.activeSelf && elapsed < 8f)
            {
                elapsed += Time.unscaledDeltaTime;
                StepConfetti(Time.unscaledDeltaTime);
                if (elapsed > 0.3f && elapsed < 2.6f && Random.value < 0.16f)
                {
                    Burst(winHalf, 5, winColor, win: true);
                }
                yield return null;
            }

            showRoutine = null;
        }

        private void SpawnWinPopups(HalfView half, Color playerColor)
        {
            ClearChildren(half.popRoot);
            StartCoroutine(PopupStamp(half, "WIN", new Vector2(-280f, 62f), 48, Gold, 0.05f));
            StartCoroutine(PopupStamp(half, "WIN", new Vector2(300f, -58f), 44, playerColor, 0.12f));
            StartCoroutine(PopupStamp(half, "1st", new Vector2(-320f, -48f), 38, Color.white, 0.18f));
            StartCoroutine(PopupStamp(half, "★", new Vector2(310f, 70f), 52, Gold, 0.08f));
            StartCoroutine(PopupStamp(half, "★", new Vector2(-70f, 82f), 40, playerColor, 0.22f));
            StartCoroutine(PopupStamp(half, "★", new Vector2(80f, -80f), 36, Gold, 0.28f));
        }

        private void SpawnDefeatPopups(HalfView half, Color playerColor)
        {
            Color muted = Color.Lerp(playerColor, DefeatInk, 0.55f);
            ClearChildren(half.popRoot);
            StartCoroutine(PopupStamp(half, "DEFEAT", new Vector2(-260f, 58f), 36, DefeatInk, 0.08f));
            StartCoroutine(PopupStamp(half, "TOO SLOW", new Vector2(270f, 64f), 32, muted, 0.16f));
            StartCoroutine(PopupStamp(half, "2nd", new Vector2(-300f, -52f), 40, Color.white, 0.22f));
            StartCoroutine(PopupStamp(half, "LOSE", new Vector2(290f, -60f), 38, muted, 0.12f));
            StartCoroutine(PopupStamp(half, "...", new Vector2(20f, 78f), 48, DefeatInk, 0.3f));
            StartCoroutine(PopupStamp(half, "NO WIN", new Vector2(-40f, -78f), 30, DefeatInk, 0.26f));
        }

        private IEnumerator PopupStamp(HalfView half, string copy, Vector2 pos, int size, Color color, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            if (half.popRoot == null || !panel.activeSelf) yield break;

            var text = CreateLabel("Pop", half.popRoot, copy, size, color);
            var rect = text.rectTransform;
            rect.anchoredPosition = pos;
            rect.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-16f, 16f));

            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime * 6f;
                float k = Mathf.Clamp01(t);
                rect.localScale = Vector3.one * Mathf.LerpUnclamped(0f, 1f, EaseOutBack(k));
                yield return null;
            }
        }

        private void Burst(HalfView half, int count, Color playerColor, bool win)
        {
            var palette = win ? WinColors : DefeatColors;
            for (int i = 0; i < count; i++)
            {
                var piece = CreateUi("Bit", half.confettiRoot);
                var image = piece.gameObject.AddComponent<Image>();
                image.raycastTarget = false;
                Color color = Random.value < 0.3f ? playerColor : palette[Random.Range(0, palette.Length)];
                color.a = Random.Range(0.55f, 0.95f);
                image.color = color;
                piece.sizeDelta = new Vector2(Random.Range(10f, 22f), Random.Range(8f, 16f));
                piece.anchoredPosition = new Vector2(Random.Range(-30f, 30f), Random.Range(-8f, 8f));
                piece.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

                float angle = win ? Random.Range(0f, Mathf.PI * 2f) : Random.Range(-2.6f, -0.55f);
                float speed = win ? Random.Range(280f, 780f) : Random.Range(80f, 260f);
                bits.Add(new Confetti
                {
                    rect = piece,
                    velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed + (win ? Vector2.up * 160f : Vector2.down * 40f),
                    spin = Random.Range(-360f, 360f),
                    life = Random.Range(1.3f, 2.5f),
                });
            }
        }

        private void StepConfetti(float dt)
        {
            for (int i = bits.Count - 1; i >= 0; i--)
            {
                var bit = bits[i];
                bit.life -= dt;
                bit.velocity += new Vector2(0f, -980f) * dt;
                bit.rect.anchoredPosition += bit.velocity * dt;
                bit.rect.Rotate(0f, 0f, bit.spin * dt);
                if (bit.life <= 0f || Mathf.Abs(bit.rect.anchoredPosition.y) > 320f)
                {
                    Destroy(bit.rect.gameObject);
                    bits.RemoveAt(i);
                    continue;
                }
                bits[i] = bit;
            }
        }

        private HalfView HalfFor(int playerIndex) => playerIndex == 1 ? p1 : p2;

        private void Build()
        {
            if (built || panel == null) return;
            built = true;

            var dim = panel.GetComponent<Image>();
            if (dim != null)
            {
                dim.color = Color.clear;
                dim.raycastTarget = false;
            }

            HideLegacy("Title");
            HideLegacy("ResultsText");

            p1 = BuildHalf("P1Half", new Vector2(0f, 0.5f), Vector2.one);
            p2 = BuildHalf("P2Half", Vector2.zero, new Vector2(1f, 0.5f));

            PlacePlayAgain(playAgainButton, p1.root);
            playAgainCopy = DuplicatePlayAgain(p2.root);

            panel.SetActive(false);
        }

        private HalfView BuildHalf(string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var root = CreateUi(name, panel.transform);
            root.anchorMin = anchorMin;
            root.anchorMax = anchorMax;
            root.offsetMin = root.offsetMax = Vector2.zero;
            root.gameObject.AddComponent<RectMask2D>();

            var strip = CreateUi("Stripe", root);
            // Full width of this player's view, a band through the middle.
            strip.anchorMin = new Vector2(0f, 0.36f);
            strip.anchorMax = new Vector2(1f, 0.64f);
            strip.offsetMin = strip.offsetMax = Vector2.zero;

            BuildChessBoard(strip, 24, 4);

            var headline = CreateLabel("Headline", strip, "WIN", 110, Gold);
            Stretch(headline.rectTransform);
            headline.rectTransform.offsetMin = new Vector2(0f, 6f);
            headline.rectTransform.offsetMax = new Vector2(0f, -6f);

            var pops = CreateUi("Pops", strip);
            Stretch(pops);
            var confetti = CreateUi("Confetti", root);
            Stretch(confetti);

            return new HalfView
            {
                root = root,
                strip = strip,
                confettiRoot = confetti,
                popRoot = pops,
                headline = headline,
            };
        }

        private void PlacePlayAgain(Button button, RectTransform parent)
        {
            if (button == null) return;
            var rect = button.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.16f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(240f, 58f);
            StylePlayAgain(button);
        }

        private Button DuplicatePlayAgain(RectTransform parent)
        {
            if (playAgainButton == null) return null;
            var copy = Instantiate(playAgainButton.gameObject, parent);
            copy.name = "PlayAgainButton";
            var button = copy.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnPlayAgainClicked);
            PlacePlayAgain(button, parent);
            return button;
        }

        private void BuildChessBoard(RectTransform parent, int cols, int rows)
        {
            var board = CreateUi("Chess", parent);
            Stretch(board);

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    var square = CreateUi($"S{x}_{y}", board).gameObject.AddComponent<Image>();
                    square.raycastTarget = false;
                    bool dark = ((x + y) % 2) == 0;
                    square.color = dark
                        ? new Color(0.05f, 0.05f, 0.05f, 0.42f)
                        : new Color(1f, 1f, 1f, 0.38f);
                    var rect = square.rectTransform;
                    rect.anchorMin = new Vector2(x / (float)cols, y / (float)rows);
                    rect.anchorMax = new Vector2((x + 1) / (float)cols, (y + 1) / (float)rows);
                    rect.offsetMin = rect.offsetMax = Vector2.zero;
                }
            }
        }

        private void HideLegacy(string name)
        {
            var child = panel.transform.Find(name);
            if (child != null) child.gameObject.SetActive(false);
        }

        private static void StylePlayAgain(Button button)
        {
            var image = button.GetComponent<Image>();
            if (image != null) image.color = new Color(0.95f, 0.18f, 0.22f);
            var label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.font = GameFonts.Display;
                label.fontSize = 28;
                label.color = Color.white;
                label.text = "PLAY AGAIN";
            }
        }

        private static Text CreateLabel(string name, Transform parent, string copy, int size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = copy;
            text.font = GameFonts.Display;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(4f, -4f);
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.4f);
            shadow.effectDistance = new Vector2(8f, -10f);
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(720f, 140f);
            return text;
        }

        private static RectTransform CreateUi(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void ClearChildren(Transform root)
        {
            if (root == null) return;
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Destroy(root.GetChild(i).gameObject);
            }
        }

        private static float EaseOutBack(float t)
        {
            const float s = 1.7f;
            t -= 1f;
            return t * t * ((s + 1f) * t + s) + 1f;
        }

        private void OnPlayAgainClicked()
        {
            GameManager.Instance.PlayAgain();
        }
    }
}
