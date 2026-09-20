using UnityEngine;

namespace MarioKart.Players
{
    /// <summary>
    /// Comic sound effects for one kart, from the CC0 clips in
    /// Resources/Audio (see CREDITS.txt there; tools/sfx/fetch_sfx.py
    /// rebuilds them). Four parts:
    ///   • one engine loop (a cartoon putt-putt) driven by the keys, not
    ///     by speed: holding the accelerator revs it up -- faster, higher,
    ///     louder; letting go, or pressing against the direction of
    ///     motion, lets it fall back to idle,
    ///   • a "skrrt" one-shot whenever a skid starts,
    ///   • a toon clang on impact, louder the harder the hit
    ///     (via KartController.Impact, like the camera shake),
    ///   • a jingle / power-down sting on pickups (KartController.PowerUp /
    ///     PowerDown).
    /// Everything is 2D: the scene has a single AudioListener (Camera_P1),
    /// so both karts must be heard equally. A missing clip logs one warning
    /// and that sound is simply skipped -- the race never depends on audio.
    /// </summary>
    [RequireComponent(typeof(KartController))]
    public class KartAudio : MonoBehaviour
    {
        [Header("Mix")]
        [Range(0f, 1f)] public float masterVolume = 0.8f;
        [Tooltip("Pitch offset for this kart so the two engines don't phase against each other (P1 −0.04, P2 +0.04).")]
        public float pitchOffset = 0f;

        [Header("Engine")]
        [Range(0f, 1f)] public float idleVolume = 0.45f;
        [Range(0f, 1f)] public float revVolume = 0.7f;
        [Tooltip("Idle volume while the kart is frozen (lobby, countdown, results).")]
        [Range(0f, 1f)] public float parkedVolume = 0.2f;
        [Tooltip("Engine pitch at idle and flat out. The loop is a putt-putt, so higher = faster putts.")]
        public float idlePitch = 0.9f;
        public float revPitch = 1.9f;
        [Tooltip("How fast the engine volume fades toward its target, in volume units per second.")]
        public float volumeSlew = 3f;
        [Tooltip("Seconds for the engine to rev from idle to full while the accelerator is held.")]
        public float revUpTime = 1.2f;
        [Tooltip("Seconds to fall back to idle after the accelerator is released.")]
        public float revDownTime = 1.5f;
        [Tooltip("Seconds to fall back to idle while pressing against the direction of motion (braking).")]
        public float brakeDownTime = 0.5f;

        [Header("One-shots")]
        [Range(0f, 1f)] public float screechVolume = 0.6f;
        [Range(0f, 1f)] public float hitVolume = 0.9f;
        [Range(0f, 1f)] public float pickupVolume = 0.8f;
        [Tooltip("Minimum impact speed (m/s) for a hit sound; matches KartSkidEffect.")]
        public float minImpactSpeed = 2f;
        [Tooltip("Shortest gap between two hit sounds, so scraping a wall doesn't machine-gun.")]
        public float hitCooldown = 0.15f;

        private KartController kart;
        private Rigidbody rb;
        private AudioSource engineSource, oneShotSource;
        private AudioClip[] screechClips, hitClips;
        private AudioClip powerUpClip, powerDownClip;
        private bool wasSkidding;
        private float lastHitTime = -10f;
        private float rev; // 0 = idle, 1 = flat out; follows the keys, not the speed

        private void Awake()
        {
            kart = GetComponent<KartController>();
            rb = GetComponent<Rigidbody>();

            engineSource = CreateLoop("Audio_Engine", LoadClip("kart_engine"));
            oneShotSource = CreateSource("Audio_OneShot");

            screechClips = LoadVariants("kart_screech");
            hitClips = LoadVariants("kart_hit");
            powerUpClip = LoadClip("pickup_powerup");
            powerDownClip = LoadClip("pickup_powerdown");
        }

        private void OnEnable()
        {
            kart.Impact += OnImpact;
            kart.PowerUp += OnPowerUp;
            kart.PowerDown += OnPowerDown;
        }

        private void OnDisable()
        {
            kart.Impact -= OnImpact;
            kart.PowerUp -= OnPowerUp;
            kart.PowerDown -= OnPowerDown;
        }

        private void Update()
        {
            bool parked = rb == null || rb.isKinematic;

            // ---- Rev: follows the keys. Accelerator down (and not fighting
            // the current motion) revs up; nothing pressed eases back to
            // idle; pressing against the motion (braking, or throttle while
            // rolling backwards) drops it quickly. Speed is deliberately
            // not consulted, so the note holds steady at top speed. ----
            float pressed = kart.Throttle > 0f ? 1f : kart.Brake > 0f ? -1f : 0f;
            float motion = Mathf.Abs(kart.ForwardSpeed) < 0.5f ? 0f : Mathf.Sign(kart.ForwardSpeed);
            bool againstMotion = pressed != 0f && motion != 0f && pressed != motion;

            float revTarget, revTime;
            if (parked || pressed == 0f)
            {
                revTarget = 0f;
                revTime = revDownTime;
            }
            else if (againstMotion)
            {
                revTarget = 0f;
                revTime = brakeDownTime;
            }
            else
            {
                revTarget = 1f;
                revTime = revUpTime;
            }
            rev = Mathf.MoveTowards(rev, revTarget, Time.deltaTime / Mathf.Max(0.05f, revTime));

            // ---- Engine: one putt-putt loop that gets faster, higher and
            // louder with the revs. A single source, so there is no second
            // loop to restart mid-phrase or beat against this one. ----
            float volumeTarget = parked ? parkedVolume : Mathf.Lerp(idleVolume, revVolume, rev);
            SetLoop(engineSource, volumeTarget, Mathf.Lerp(idlePitch, revPitch, rev), volumeSlew * Time.deltaTime);

            // ---- Screech: one "skrrt" per skid, not one per frame. ----
            bool skidding = kart.IsSkidding && Mathf.Abs(kart.ForwardSpeed) > 1f;
            if (skidding && !wasSkidding)
            {
                PlayVariant(screechClips, screechVolume);
            }
            wasSkidding = skidding;
        }

        private void OnImpact(float impactSpeed)
        {
            if (impactSpeed < minImpactSpeed) return;
            if (Time.time - lastHitTime < hitCooldown) return;
            lastHitTime = Time.time;
            float strength = Mathf.InverseLerp(minImpactSpeed, kart.maxSpeed, impactSpeed);
            PlayVariant(hitClips, hitVolume * Mathf.Lerp(0.5f, 1f, strength));
        }

        private void OnPowerUp()
        {
            PlayOneShot(powerUpClip, pickupVolume, 1f);
        }

        private void OnPowerDown()
        {
            PlayOneShot(powerDownClip, pickupVolume, 1f);
        }

        // ------------------------------------------------------------------

        private void SetLoop(AudioSource source, float targetVolume, float pitch, float slew)
        {
            if (source == null) return;
            source.volume = Mathf.MoveTowards(source.volume, targetVolume * masterVolume, slew);
            source.pitch = pitch + pitchOffset;
        }

        private void PlayVariant(AudioClip[] clips, float volume)
        {
            if (clips == null || clips.Length == 0) return;
            // Cosmetic jitter only. ADR 0004's WorldRandom rule is about
            // world *generation* staying deterministic; nothing here feeds
            // back into it, so UnityEngine.Random is fine.
            var clip = clips[Random.Range(0, clips.Length)];
            PlayOneShot(clip, volume, Random.Range(0.94f, 1.06f));
        }

        private void PlayOneShot(AudioClip clip, float volume, float pitch)
        {
            if (clip == null || oneShotSource == null) return;
            oneShotSource.pitch = pitch + pitchOffset;
            oneShotSource.PlayOneShot(clip, volume * masterVolume);
        }

        private AudioSource CreateSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, worldPositionStays: false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f; // one listener for two karts: everything is 2D
            return source;
        }

        private AudioSource CreateLoop(string name, AudioClip clip)
        {
            if (clip == null) return null;
            var source = CreateSource(name);
            source.clip = clip;
            source.loop = true;
            source.volume = 0f;
            source.Play();
            // Start each kart somewhere else in the loop so the two engines
            // don't run in lockstep (cosmetic -- see PlayVariant on Random).
            source.time = Random.Range(0f, clip.length * 0.9f);
            return source;
        }

        private static AudioClip LoadClip(string name)
        {
            var clip = Resources.Load<AudioClip>("Audio/" + name);
            if (clip == null)
            {
                Debug.LogWarning($"KartAudio: Resources/Audio/{name} not found -- run tools/sfx/fetch_sfx.py. That sound is skipped.");
            }
            return clip;
        }

        /// <summary>Loads name_0, name_1, ... until one is missing.</summary>
        private static AudioClip[] LoadVariants(string name)
        {
            var clips = new System.Collections.Generic.List<AudioClip>();
            for (int i = 0; ; i++)
            {
                var clip = Resources.Load<AudioClip>($"Audio/{name}_{i}");
                if (clip == null) break;
                clips.Add(clip);
            }
            if (clips.Count == 0)
            {
                Debug.LogWarning($"KartAudio: no Resources/Audio/{name}_N clips found -- run tools/sfx/fetch_sfx.py. That sound is skipped.");
            }
            return clips.ToArray();
        }
    }
}
