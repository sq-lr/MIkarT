using System;
using System.Collections.Generic;
using UnityEngine;

namespace MarioKart.Racing
{
    public struct PlayerResult
    {
        public int playerIndex;
        public float finishTime;
        public int placement;
    }

    public class RaceResult
    {
        public List<PlayerResult> placements = new List<PlayerResult>();
    }

    /// <summary>
    /// Aggregates both players' LapManagers into a single race outcome.
    /// Waits for BOTH players to finish before firing OnRaceFinished, so the
    /// results screen always has a full placement list (the spec did not
    /// specify first-finisher-ends-race vs wait-for-both; this is the
    /// judgment call documented in docs/decisions/).
    /// </summary>
    public class RaceManager : MonoBehaviour
    {
        [SerializeField] private LapManager[] players;
        [SerializeField] private int lapCount = 3;

        public event Action<RaceResult> OnRaceFinished;

        private readonly List<PlayerResult> finishedPlayers = new List<PlayerResult>();

        private void Start()
        {
            foreach (var player in players)
            {
                player.Configure(GetCheckpointCountFor(player), lapCount);
                player.OnPlayerFinishedRace += HandlePlayerFinished;
            }
        }

        /// <summary>
        /// Clear the previous outcome and every player's lap progress.
        /// RaceBootstrap calls this on WorldReady, before the countdown.
        /// </summary>
        public void ResetRace()
        {
            finishedPlayers.Clear();
            foreach (var player in players)
            {
                player.ResetProgress();
            }
        }

        private int GetCheckpointCountFor(LapManager player)
        {
            // Placeholder: real checkpoint count comes from the generated
            // track (see WorldGenerator / TrackGenerator).
            return 8;
        }

        private void HandlePlayerFinished(int playerIndex, float finishTime)
        {
            finishedPlayers.Add(new PlayerResult
            {
                playerIndex = playerIndex,
                finishTime = finishTime,
                placement = finishedPlayers.Count + 1,
            });

            if (finishedPlayers.Count >= players.Length)
            {
                var result = new RaceResult { placements = new List<PlayerResult>(finishedPlayers) };
                OnRaceFinished?.Invoke(result);
            }
        }
    }
}
