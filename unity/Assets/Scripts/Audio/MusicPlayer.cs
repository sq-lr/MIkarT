using MarioKart.Core;
using MarioKart.World;
using UnityEngine;

namespace MarioKart.Audio
{
    /// <summary>
    /// Plays the soundtrack that matches the world's mood
    /// (WorldRecipe.world.mood, chosen by the vision model from the photo and
    /// the player's description -- see docs/decisions/0010-mood-soundtracks.md).
    /// One CC0 loop per mood lives in Resources/Audio/Music/music_&lt;mood&gt;
    /// (CREDITS.txt there; tools/sfx/fetch_sfx.py fetches them).
    ///
    /// Purely reactive to GameManager's state: fades in on WorldReady (under
    /// the countdown), plays through the race, ducks on Results, fades out
    /// back at the lobby. An unknown mood falls back to "energetic"; a missing
    /// clip logs one warning and stays silent -- the game never depends on
    /// music.
    /// </summary>
    public class MusicPlayer : MonoBehaviour
    {
        [SerializeField] private WorldGenerator worldGenerator;

        [Range(0f, 1f)] public float volume = 0.55f;
        [Tooltip("Volume on the results screen, so the winner text isn't fighting the music.")]
        [Range(0f, 1f)] public float resultsVolume = 0.25f;
        public float fadeInSeconds = 1.5f;
        public float fadeOutSeconds = 0.8f;

        private const string FallbackMood = "energetic";

        private AudioSource source;
        private float targetVolume;
        private float fadeRate;
        private bool stopWhenSilent;

        private void Awake()
        {
            if (worldGenerator == null) worldGenerator = FindFirstObjectByType<WorldGenerator>();

            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f; // one listener, both players: music is 2D
            source.volume = 0f;
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
            switch (state)
            {
                case GameState.WorldReady:
                    StartTrackFor(CurrentMood());
                    break;
                case GameState.Countdown:
                case GameState.Racing:
                case GameState.Finished:
                    if (source.isPlaying) FadeTo(volume, fadeInSeconds);
                    break;
                case GameState.Results:
                    FadeTo(resultsVolume, fadeOutSeconds);
                    break;
                default: // Boot, Input, Generating: no world, no music
                    FadeTo(0f, fadeOutSeconds, stopAfter: true);
                    break;
            }
        }

        private void Update()
        {
            if (source == null || Mathf.Approximately(source.volume, targetVolume))
            {
                if (stopWhenSilent && source != null && source.volume <= 0f && source.isPlaying)
                {
                    source.Stop();
                    stopWhenSilent = false;
                }
                return;
            }
            source.volume = Mathf.MoveTowards(source.volume, targetVolume, fadeRate * Time.deltaTime);
        }

        private string CurrentMood()
        {
            var recipe = worldGenerator != null ? worldGenerator.CurrentRecipe : null;
            string mood = recipe != null && recipe.world != null ? recipe.world.mood : null;
            return string.IsNullOrEmpty(mood) ? FallbackMood : mood;
        }

        private void StartTrackFor(string mood)
        {
            var clip = Resources.Load<AudioClip>("Audio/Music/music_" + mood);
            if (clip == null && mood != FallbackMood)
            {
                Debug.LogWarning($"MusicPlayer: no soundtrack for mood '{mood}' (Resources/Audio/Music/music_{mood}); using '{FallbackMood}'.");
                clip = Resources.Load<AudioClip>("Audio/Music/music_" + FallbackMood);
            }
            if (clip == null)
            {
                Debug.LogWarning("MusicPlayer: no soundtrack clips found under Resources/Audio/Music -- run tools/sfx/fetch_sfx.py. Music is off.");
                FadeTo(0f, fadeOutSeconds, stopAfter: true);
                return;
            }

            if (source.clip != clip || !source.isPlaying)
            {
                source.clip = clip;
                source.volume = 0f;
                source.Play();
            }
            FadeTo(volume, fadeInSeconds);
        }

        private void FadeTo(float target, float seconds, bool stopAfter = false)
        {
            targetVolume = target;
            fadeRate = Mathf.Abs(source.volume - target) / Mathf.Max(0.01f, seconds);
            stopWhenSilent = stopAfter;
        }
    }
}
