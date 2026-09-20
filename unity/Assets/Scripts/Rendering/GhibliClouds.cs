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
        private const int DefaultCount = 11;
        private const float HeightMin = 38f;
        private const float HeightMax = 72f;

        public void Rebuild(GeneratedTrack track, int seed)
        {
            Rebuild(track, seed, "sunny");
        }

        public void Rebuild(GeneratedTrack track, int seed, string sky)
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

            int count = sky == "cloudy" ? 18 : sky == "sunset" ? 13 : sky == "night" ? 5 : DefaultCount;
            var highlightColor = sky == "sunset"
                ? new Color(1f, 0.68f, 0.60f)
                : sky == "night"
                    ? new Color(0.28f, 0.38f, 0.58f)
                    : GhibliLook.Cloud;
            var shadowColor = sky == "sunset"
                ? new Color(0.44f, 0.28f, 0.48f)
                : sky == "night"
                    ? new Color(0.08f, 0.14f, 0.28f)
                    : new Color(0.54f, 0.68f, 0.78f);
            var cloudShader = Shader.Find("MarioKart/GhibliCloud");
            var highlightMat = GhibliLook.Lit(highlightColor);
            var shadowMat = GhibliLook.Lit(shadowColor);
            if (cloudShader != null)
            {
                highlightMat = new Material(cloudShader);
                highlightMat.SetColor("_Highlight", highlightColor);
                highlightMat.SetColor("_Midtone", sky == "sunset" ? new Color(0.88f, 0.56f, 0.58f) : new Color(0.78f, 0.86f, 0.88f));
                highlightMat.SetColor("_Shadow", shadowColor);
                highlightMat.SetVector("_LightDirection", new Vector4(-0.35f, 0.8f, -0.2f, 0f));
                shadowMat = highlightMat;
            }

            for (int i = 0; i < count; i++)
            {
                float angle = (i / (float)count) * Mathf.PI * 2f + rng.NextRange(-0.2f, 0.2f);
                float dist = radius + rng.NextRange(-8f, 28f);
                var pos = new Vector3(Mathf.Cos(angle) * dist, rng.NextRange(HeightMin, HeightMax), Mathf.Sin(angle) * dist);
                float sx = rng.NextRange(16f, sky == "cloudy" ? 42f : 34f);
                float sy = rng.NextRange(5f, sky == "cloudy" ? 14f : 10f);
                float sz = rng.NextRange(12f, sky == "cloudy" ? 32f : 26f);

                var cloud = new GameObject($"Cloud_{i}");
                cloud.transform.SetParent(transform, worldPositionStays: false);
                cloud.transform.position = pos;
                CreateBlob(cloud.transform, "Shadow", new Vector3(0f, -sy * 0.18f, 0f),
                    new Vector3(sx, sy * 0.72f, sz), shadowMat);
                CreateBlob(cloud.transform, "Highlight", new Vector3(-sx * 0.12f, sy * 0.18f, -sz * 0.08f),
                    new Vector3(sx * 0.82f, sy * 0.72f, sz * 0.78f), highlightMat);
                CreateBlob(cloud.transform, "Tower", new Vector3(sx * 0.18f, sy * 0.30f, sz * 0.05f),
                    new Vector3(sx * 0.48f, sy * 0.68f, sz * 0.48f), highlightMat);
            }
        }

        private static void CreateBlob(Transform parent, string name, Vector3 localPosition, Vector3 scale, Material material)
        {
            var blob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            blob.name = name;
            blob.transform.SetParent(parent, worldPositionStays: false);
            blob.transform.localPosition = localPosition;
            blob.transform.localScale = scale;
            var collider = blob.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            blob.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
