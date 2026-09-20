using System;
using UnityEngine;

namespace MarioKart.Racing
{
    /// <summary>
    /// Tracks one player's progress around the loop. Checkpoints must be
    /// passed in order, so driving backward through the finish line does
    /// not award a lap. A lap is awarded on crossing checkpoint 0 (the
    /// checkered stripe) after every other gate, not on the last gate
    /// before it -- so the counter, chime and finish all fire on the line.
    /// </summary>
    public class LapManager : MonoBehaviour
    {
        public int playerIndex = 1;
        [SerializeField] private int totalCheckpoints = 8;
        [SerializeField] private int totalLaps = 3;

        private int nextExpectedCheckpoint;
        private bool startCrossed; // the grid sits just behind checkpoint 0; that first crossing is not a lap
        public int CurrentLap { get; private set; }
        public int TotalLaps => totalLaps;
        public bool Finished { get; private set; }

        public event Action<int> OnLapCompleted;
        public event Action<int, float> OnPlayerFinishedRace;

        public void Configure(int checkpointCount, int lapCount)
        {
            totalCheckpoints = checkpointCount;
            totalLaps = lapCount;
        }

        /// <summary>
        /// Back to the start line. Called by RaceManager.ResetRace before
        /// every race so "Play Again" starts from lap 0.
        /// </summary>
        public void ResetProgress()
        {
            nextExpectedCheckpoint = 0;
            startCrossed = false;
            CurrentLap = 0;
            Finished = false;
        }

        public void OnCheckpointPassed(int checkpointIndex)
        {
            if (Finished || checkpointIndex != nextExpectedCheckpoint) return;

            nextExpectedCheckpoint = (nextExpectedCheckpoint + 1) % totalCheckpoints;

            if (checkpointIndex == 0)
            {
                // Ordered passing means reaching the line again as the
                // expected gate implies a full lap behind it.
                if (!startCrossed)
                {
                    startCrossed = true;
                    return;
                }

                CurrentLap++;
                OnLapCompleted?.Invoke(CurrentLap);

                if (CurrentLap >= totalLaps)
                {
                    Finished = true;
                    OnPlayerFinishedRace?.Invoke(playerIndex, Time.time);
                }
            }
        }
    }
}
