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
    /// The first kart to finish every lap gets a winner banner; the race
    /// keeps running so the other player can still drive (and take 2nd).
    /// </summary>
    public class RaceManager : MonoBehaviour
    {
        [SerializeField] private LapManager[] players;
        [SerializeField] private int lapCount = 3;

        public event Action<RaceResult> OnRaceFinished;

        private readonly List<PlayerResult> finishedPlayers = new List<PlayerResult>();
        private float raceStartTime;

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

        /// <summary>Call when the countdown ends so finish times are race-elapsed, not session time.</summary>
        public void MarkRacing()
        {
            raceStartTime = Time.time;
        }

        private int GetCheckpointCountFor(LapManager player)
        {
            // Placeholder: real checkpoint count comes from the generated
            // track (see WorldGenerator / TrackGenerator).
            return 8;
        }

        private void HandlePlayerFinished(int playerIndex, float finishTime)
        {
            if (finishedPlayers.Exists(p => p.playerIndex == playerIndex)) return;

            finishedPlayers.Add(new PlayerResult
            {
                playerIndex = playerIndex,
                finishTime = Mathf.Max(0f, finishTime - raceStartTime),
                placement = finishedPlayers.Count + 1,
            });

            var result = new RaceResult { placements = new List<PlayerResult>(finishedPlayers) };
            OnRaceFinished?.Invoke(result);
        }
    }
}
