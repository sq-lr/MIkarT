using UnityEngine;

namespace MarioKart.Racing
{
    /// <summary>
    /// Trigger volume placed along the track by TrackGenerator. Requires a
    /// Collider with isTrigger = true on the same GameObject (wired up
    /// manually per docs/development.md, since colliders can't be
    /// hand-authored the same way scripts can).
    /// </summary>
    public class Checkpoint : MonoBehaviour
    {
        public int checkpointIndex;

        private void OnTriggerEnter(Collider other)
        {
            var lapManager = other.GetComponentInParent<LapManager>();
            if (lapManager != null)
            {
                lapManager.OnCheckpointPassed(checkpointIndex);
            }
        }
    }
}
