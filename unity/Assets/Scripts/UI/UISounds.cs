using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MarioKart.UI
{
    /// <summary>
    /// Hover / press sounds for every button and toggle. Each UI script
    /// calls <see cref="Attach"/> on its own controls in Awake; that adds a
    /// small listener which plays the clips through one shared 2D
    /// AudioSource. Other UI one-shots (the countdown) go through
    /// <see cref="Play"/> on the same source. Clips come from
    /// Resources/Audio (CC0 -- see CREDITS.txt there). Like GameFonts, a
    /// missing clip logs one warning and is skipped, so the UI never
    /// depends on audio.
    /// </summary>
    public static class UISounds
    {
        private const string HoverPath = "Audio/ui_hover";
        private const string ClickPath = "Audio/ui_click";

        public static float hoverVolume = 0.5f;
        public static float clickVolume = 0.8f;

        private static AudioSource source;
        private static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();

        /// <summary>Give a button or toggle hover and press sounds. Safe to call twice.</summary>
        public static void Attach(Selectable control)
        {
            if (control == null) return;
            if (control.GetComponent<UISoundListener>() == null)
            {
                control.gameObject.AddComponent<UISoundListener>();
            }
        }

        public static void PlayHover() => Play(HoverPath, hoverVolume);
        public static void PlayClick() => Play(ClickPath, clickVolume);

        /// <summary>Play a clip from Resources by path (e.g. "Audio/countdown_beep") on the shared UI source.</summary>
        public static void Play(string path, float volume, float pitch = 1f)
        {
            var clip = Load(path);
            if (clip == null) return;
            if (source == null)
            {
                var go = new GameObject("UISounds");
                Object.DontDestroyOnLoad(go);
                source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
            }
            source.pitch = pitch;
            source.PlayOneShot(clip, volume);
        }

        private static AudioClip Load(string path)
        {
            if (clips.TryGetValue(path, out var cached)) return cached;
            var clip = Resources.Load<AudioClip>(path);
            if (clip == null)
            {
                Debug.LogWarning($"UISounds: '{path}' not found under Resources -- run tools/sfx/fetch_sfx.py. That sound is skipped.");
            }
            clips[path] = clip; // null too, so a missing clip warns once
            return clip;
        }
    }

    /// <summary>
    /// The per-control half of UISounds: mouse-over and keyboard focus play
    /// the hover tick, a click or Enter/Space plays the press chime. Silent
    /// while the control is non-interactable (e.g. Generate before an image
    /// is picked), matching how it looks.
    /// </summary>
    [RequireComponent(typeof(Selectable))]
    public class UISoundListener : MonoBehaviour,
        IPointerEnterHandler, IPointerClickHandler, ISelectHandler, ISubmitHandler
    {
        private Selectable control;

        private void Awake()
        {
            control = GetComponent<Selectable>();
        }

        private bool Active => control != null && control.IsInteractable();

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Active) UISounds.PlayHover();
        }

        public void OnSelect(BaseEventData eventData)
        {
            if (Active) UISounds.PlayHover();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Active && eventData.button == PointerEventData.InputButton.Left) UISounds.PlayClick();
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (Active) UISounds.PlayClick();
        }
    }
}
