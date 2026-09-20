using System.Collections.Generic;
using MarioKart.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MarioKart.UI
{
    /// <summary>
    /// Shown on GameState.Boot. Purely reactive to GameManager's state --
    /// never mutates game state on its own. The scene bakes a title, a
    /// controls hint and a Start button; Awake dresses them up as a comic
    /// cover (halftone paper, the title in a burst inside a caption box,
    /// the hint in a speech bubble, word bursts around it) using
    /// ComicStyle, and every time the screen appears the pieces slam in.
    /// </summary>
    public class LobbyUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Button startButton;

        private bool built;
        private Coroutine entrance;
        private readonly List<ComicStyle.Slam> slams = new List<ComicStyle.Slam>();

        private void Awake()
        {
            startButton.onClick.AddListener(OnStartClicked);
            UISounds.Attach(startButton);
            Decorate();
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
            bool show = state == GameState.Boot;
            if (show && !panel.activeSelf)
            {
                panel.SetActive(true);
                RestartEntrance();
            }
            else if (!show)
            {
                StopEntrance();
                panel.SetActive(false);
            }
        }

        private void RestartEntrance()
        {
            StopEntrance();
            if (slams.Count > 0) entrance = StartCoroutine(ComicStyle.PlaySlams(slams));
        }

        private void StopEntrance()
        {
            if (entrance != null)
            {
                StopCoroutine(entrance);
                entrance = null;
            }
            ComicStyle.SnapToRest(slams);
        }

        /// <summary>
        /// Restyles the baked hierarchy in place (no scene rebuild needed).
        /// Missing baked children are skipped with a warning so an older
        /// scene still gets a playable, if plainer, lobby.
        /// </summary>
        private void Decorate()
        {
            if (built || panel == null) return;
            built = true;
            var root = panel.transform;

            ComicStyle.AddHalftoneBackdrop(panel, ComicStyle.Paper, new Color(ComicStyle.Red.r, ComicStyle.Red.g, ComicStyle.Red.b, 0.22f), dotScale: 0.6f);

            // The big yellow burst the title sits in.
            var titleBurst = ComicStyle.MakeImage("TitleBurst", root, ComicStyle.Burst(24, 0.80f),
                new Color(ComicStyle.Yellow.r, ComicStyle.Yellow.g, ComicStyle.Yellow.b, 0.95f), Image.Type.Simple);
            ComicStyle.Place(titleBurst.rectTransform, new Vector2(0f, 150f), new Vector2(1500f, 520f), -2f);

            var vroom = ComicStyle.AddWordBurst(root, "VROOM!", new Vector2(-720f, 340f), new Vector2(340f, 230f), -14f, ComicStyle.Red, points: 14, fontSize: 54);
            var pow = ComicStyle.AddWordBurst(root, "POW!", new Vector2(700f, 320f), new Vector2(260f, 220f), 12f, ComicStyle.Yellow, points: 10, fontSize: 54);
            var skrrt = ComicStyle.AddWordBurst(root, "SKRRT!", new Vector2(-680f, -250f), new Vector2(320f, 220f), 9f, ComicStyle.Blue, fontSize: 54);
            var zoom = ComicStyle.AddWordBurst(root, "ZOOM!", new Vector2(660f, -230f), new Vector2(300f, 210f), -11f, ComicStyle.Green, fontSize: 54);

            var titleBox = ComicStyle.AddCaptionBox(root, "TitleBox", new Vector2(0f, 150f), new Vector2(1240f, 170f), ComicStyle.White, -3f);
            var title = FindText("Title");
            if (title != null)
            {
                title.transform.SetParent(titleBox, false);
                ComicStyle.Stretch(title.rectTransform);
                ComicStyle.RestyleLabel(title, 96, ComicStyle.Red);
                title.alignment = TextAnchor.MiddleCenter;
            }

            var hintBubble = ComicStyle.AddSpeechBubble(root, "HintBubble", new Vector2(0f, -15f), new Vector2(700f, 100f), new Vector2(0.16f, 1f), 180f, ComicStyle.White);
            var hint = FindText("Hint");
            if (hint != null)
            {
                hint.transform.SetParent(hintBubble, false);
                ComicStyle.Stretch(hint.rectTransform, 14f);
                ComicStyle.RestyleLabel(hint, 30, ComicStyle.Ink, body: true);
                hint.alignment = TextAnchor.MiddleCenter;
            }

            var start = startButton.GetComponent<RectTransform>();
            ComicStyle.Place(start, new Vector2(0f, -185f), new Vector2(400f, 100f), -2f);
            ComicStyle.StyleButton(startButton, ComicStyle.Red, "START!", 48);
            start.SetAsLastSibling();

            // Entrance: title first, hint, then the word bursts rattle in and the button lands last.
            slams.Add(new ComicStyle.Slam(titleBurst.rectTransform, 0.00f, 0.28f, -2f, 1.35f, "Audio/kart_hit_0", 0.7f, 1.0f));
            slams.Add(new ComicStyle.Slam(titleBox, 0.00f, 0.28f, -3f, 1.35f));
            slams.Add(new ComicStyle.Slam(hintBubble, 0.12f, 0.22f, 0f, 1.35f, "Audio/ui_click", 0.5f, 1.1f));
            slams.Add(new ComicStyle.Slam(vroom, 0.18f, 0.20f, -14f, 1.6f, "Audio/kart_hit_1", 0.35f, 1.3f));
            slams.Add(new ComicStyle.Slam(pow, 0.26f, 0.20f, 12f, 1.6f, "Audio/kart_hit_3", 0.35f, 1.5f));
            slams.Add(new ComicStyle.Slam(skrrt, 0.34f, 0.20f, 9f, 1.6f, "Audio/kart_screech_2", 0.3f, 1.4f));
            slams.Add(new ComicStyle.Slam(zoom, 0.42f, 0.20f, -11f, 1.6f, "Audio/kart_hit_2", 0.35f, 1.2f));
            slams.Add(new ComicStyle.Slam(start, 0.40f, 0.28f, -2f, 1.35f, "Audio/kart_hit_2", 0.6f, 0.9f));

            // The baked panel is active on load; hide it so the first Boot
            // state is a hidden->shown edge and plays the entrance.
            panel.SetActive(false);
        }

        private Text FindText(string name)
        {
            var child = panel.transform.Find(name);
            var text = child != null ? child.GetComponent<Text>() : null;
            if (text == null) Debug.LogWarning($"LobbyUI: baked child '{name}' not found -- scene older than the script? Rebuild via MarioKart > Build Main Scene.", this);
            return text;
        }

        private void OnStartClicked()
        {
            GameManager.Instance.TransitionTo(GameState.Input);
        }
    }
}
