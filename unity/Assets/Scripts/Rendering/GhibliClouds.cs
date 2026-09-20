using MarioKart.World;
using UnityEngine;

namespace MarioKart.Rendering
{
    /// <summary>
    /// Big puffy countryside clouds, placed from the recipe seed so the
    /// same world keeps the same sky.
    /// </summary>
    public class GhibliClouds : MonoBehaviour
    {
        private const int Count = 11;
        private const float HeightMin = 38f;
        private const float HeightMax = 72f;

        public void Rebuild(GeneratedTrack track, int seed)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }

            if (track?.controlPoints == null || track.controlPoints.Count == 0) return;

            var rng = new WorldRandom(WorldRandom.DeriveSeed(seed, "ghibli-clouds"));
            float radius = 0f;
            foreach (var p in track.controlPoints)
            {
                radius = Mathf.Max(radius, p.magnitude);
            }
            radius += 40f;

            var mat = GhibliLook.Lit(GhibliLook.Cloud);
            if (mat.HasProperty("_Fill")) mat.SetFloat("_Fill", 0.45f);

            for (int i = 0; i < Count; i++)
            {
                float angle = (i / (float)Count) * Mathf.PI * 2f + rng.NextRange(-0.2f, 0.2f);
                float dist = radius + rng.NextRange(-8f, 28f);
                var pos = new Vector3(Mathf.Cos(angle) * dist, rng.NextRange(HeightMin, HeightMax), Mathf.Sin(angle) * dist);
                float sx = rng.NextRange(16f, 34f);
                float sy = rng.NextRange(5f, 10f);
                float sz = rng.NextRange(12f, 26f);

                var cloud = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                cloud.name = $"Cloud_{i}";
                cloud.transform.SetParent(transform, worldPositionStays: false);
                cloud.transform.position = pos;
                cloud.transform.localScale = new Vector3(sx, sy, sz);
                var col = cloud.GetComponent<Collider>();
                if (col != null) Destroy(col);
                cloud.GetComponent<Renderer>().sharedMaterial = mat;
            }
        }
    }
}
