using UnityEngine;

namespace MarioKart.Rendering
{
    /// <summary>
    /// Watercolor grade on each split-screen camera: lifted blacks, warm
    /// midtones, soft vignette. Built-in RP OnRenderImage.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    public class GhibliPostEffect : MonoBehaviour
    {
        [Range(0.7f, 1.1f)] public float lift = 0.90f;
        [Range(0f, 1f)] public float warmth = 0.38f;
        [Range(0f, 1.5f)] public float saturation = 0.86f;
        [Range(0f, 0.5f)] public float vignette = 0.16f;

        private Material material;

        private void OnEnable()
        {
            var shader = Shader.Find(GhibliLook.PostShaderName);
            if (shader != null) material = new Material(shader);
        }

        private void OnDisable()
        {
            if (material != null)
            {
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
                material = null;
            }
        }

        private void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (material == null)
            {
                Graphics.Blit(src, dst);
                return;
            }

            material.SetFloat("_Lift", lift);
            material.SetFloat("_Warmth", warmth);
            material.SetFloat("_Saturation", saturation);
            material.SetFloat("_Vignette", vignette);
            Graphics.Blit(src, dst, material);
        }
    }
}
