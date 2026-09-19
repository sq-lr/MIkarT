using MarioKart.Players;
using UnityEngine;

namespace MarioKart.World
{
    /// <summary>
    /// Lives on each barrier wall built by TrackMeshBuilder. Physics keeps
    /// the kart on the track; this adds the penalty: a speed loss on impact
    /// and a speed cap while scraping along the wall.
    /// </summary>
    public class TrackBarrier : MonoBehaviour
    {
        private void OnCollisionEnter(Collision collision)
        {
            var kart = collision.rigidbody != null ? collision.rigidbody.GetComponent<KartController>() : null;
            if (kart != null) kart.OnBarrierHit();
        }

        private void OnCollisionStay(Collision collision)
        {
            var kart = collision.rigidbody != null ? collision.rigidbody.GetComponent<KartController>() : null;
            if (kart != null) kart.OnBarrierScrape();
        }
    }
}
