using System.Collections;
using MarioKart.CameraSystem;
using MarioKart.Core;
using MarioKart.World;
using UnityEngine;

namespace MarioKart.Racing
{
    /// <summary>
    /// The glue between "world generated" and "race running". Nothing else
    /// owns this: GameManager only exposes BeginCountdown/BeginRace, the UI
    /// never mutates state, and WorldGenerator only knows about the track.
    ///
    /// As soon as WorldGenerator has built a track (its OnWorldReady, which
    /// fires before any wait for generated meshes): park both karts on the
    /// start line and snap the cameras behind them, so the Generating
    /// screen's dimmed backdrop shows the start line rather than the
    /// cameras buried in the terrain at the loop's centre. On the WorldReady
    /// state: re-park (frozen), reset lap progress, and start the countdown.
    /// On Racing: unfreeze them. In every other state the karts stay
    /// kinematic so nobody can drive around the lobby or the results screen.
    /// </summary>
    public class RaceBootstrap : MonoBehaviour
    {
        [SerializeField] private WorldGenerator worldGenerator;
        [SerializeField] private RaceManager raceManager;
        [SerializeField] private Rigidbody[] karts = new Rigidbody[2];
        [SerializeField] private PlayerCamera[] cameras = new PlayerCamera[2];

        // How far behind checkpoint 0 the grid sits, so the first thing a
        // kart does is cross checkpoint 0 (LapManager expects them in order).
        [SerializeField] private float startLineOffset = 4f;

        private Coroutine countdown;

        private void Start()
        {
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null) cameras[i].SetViewportForPlayer(i + 1);
            }

            SetKartsFrozen(true);

            GameManager.Instance.OnStateChanged += HandleStateChanged;
            if (worldGenerator != null) worldGenerator.OnWorldReady += HandleWorldGenerated;
        }

        private void OnDestroy()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStateChanged -= HandleStateChanged;
            }
            if (worldGenerator != null) worldGenerator.OnWorldReady -= HandleWorldGenerated;
        }

        /// <summary>
        /// The track exists (placeholders and all); get the karts and cameras
        /// onto it now, before the Generating screen lifts.
        /// </summary>
        private void HandleWorldGenerated()
        {
            PlaceKartsOnStartLine();
            foreach (var cam in cameras) if (cam != null) cam.SnapToTarget();
        }

        private void HandleStateChanged(GameState state)
        {
            switch (state)
            {
                case GameState.WorldReady:
                    PlaceKartsOnStartLine();
                    foreach (var cam in cameras) if (cam != null) cam.SnapToTarget();
                    SetKartsFrozen(true);
                    if (raceManager != null) raceManager.ResetRace();
                    GameManager.Instance.BeginCountdown();
                    break;

                case GameState.Countdown:
                    if (countdown != null) StopCoroutine(countdown);
                    countdown = StartCoroutine(RunCountdown());
                    break;

                case GameState.Racing:
                    SetKartsFrozen(false);
                    break;

                default:
                    SetKartsFrozen(true);
                    break;
            }
        }

        private IEnumerator RunCountdown()
        {
            float seconds = GameManager.Instance.Config.countdownSeconds;
            for (int remaining = Mathf.CeilToInt(seconds); remaining > 0; remaining--)
            {
                Debug.Log($"Race starts in {remaining}...");
                yield return new WaitForSeconds(1f);
            }
            Debug.Log("GO!");
            countdown = null;
            GameManager.Instance.BeginRace();
        }

        private void PlaceKartsOnStartLine()
        {
            var track = worldGenerator != null ? worldGenerator.CurrentTrack : null;
            if (track == null || track.controlPoints == null || track.controlPoints.Count == 0)
            {
                Debug.LogWarning("RaceBootstrap: no generated track; leaving karts where they are");
                return;
            }

            Vector3 line = track.controlPoints[0];
            Vector3 forward = WorldGenerator.TangentAt(track, 0);
            Vector3 across = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 gridCenter = line - forward * startLineOffset;

            for (int i = 0; i < karts.Length; i++)
            {
                var kart = karts[i];
                if (kart == null) continue;

                // Two-wide grid centred on the track: P1 left, P2 right.
                float lateral = (i - (karts.Length - 1) * 0.5f) * (track.width * 0.5f);
                Vector3 position = gridCenter + across * lateral;

                // Flush on the road surface, tilted with it: the karts are
                // kinematic until the countdown ends, so physics won't
                // settle them onto a hill for us.
                float surface = track.SurfaceFrameAt(position, out Vector3 slopeForward, out Vector3 normal);
                position.y = surface;
                position += normal * (kart.transform.localScale.y * 0.5f + 0.05f);

                kart.transform.SetPositionAndRotation(position, Quaternion.LookRotation(slopeForward, normal));
            }
        }

        private void SetKartsFrozen(bool frozen)
        {
            foreach (var kart in karts)
            {
                if (kart == null) continue;
                // Kinematic bodies reject velocity writes (Unity warns), so
                // only zero it while the body is still dynamic.
                if (frozen && !kart.isKinematic)
                {
                    kart.linearVelocity = Vector3.zero;
                    kart.angularVelocity = Vector3.zero;
                }
                kart.isKinematic = frozen;
            }
        }
    }
}
