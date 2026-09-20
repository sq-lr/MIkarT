using MarioKart.Core;
using UnityEngine;

namespace MarioKart.Rendering
{
    /// <summary>
    /// Faint static paper grain over a camera's frame (docs/decisions/0008).
    /// Deliberately subtle and *not* a halftone: it reads as paper stock
    /// under the toon shading rather than as a second dot pattern competing
    /// with the shadow-band dots in ToonCommon.cginc. The grain never
    /// animates, so it does not read as film noise.
    ///
    /// One per player camera (MainSceneBuilder adds it; PlayerCamera also
    /// ensures it at runtime). Strength comes from GameConfig each frame so
    /// it can be tuned live in the Inspector; 0 skips the blit entirely. If
    /// the shader is missing the effect disables itself with one warning.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PaperGrainEffect : MonoBehaviour
    {
        private const string ShaderName = "Hidden/MarioKart/PaperGrain";
        private const int GrainSize = 256;

        private static readonly int GrainTexId = Shader.PropertyToID("_GrainTex");
        private static readonly int StrengthId = Shader.PropertyToID("_Strength");
        private static readonly int GrainScaleId = Shader.PropertyToID("_GrainScale");

        // Shared across cameras: the texture is deterministic and read-only.
        private static Texture2D grain;
        private static int grainUsers;

        private Material material;

        private void OnEnable()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null || !shader.isSupported)
            {
                Debug.LogWarning($"PaperGrainEffect: shader '{ShaderName}' unavailable; disabling grain on {name}");
                enabled = false;
                return;
            }

            if (grain == null) grain = BuildGrain(GrainSize);
            grainUsers++;

            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture(GrainTexId, grain);
        }

        private void OnDisable()
        {
            if (material != null)
            {
                Destroy(material);
                material = null;
                if (--grainUsers <= 0 && grain != null)
                {
                    Destroy(grain);
                    grain = null;
                    grainUsers = 0;
                }
            }
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            var config = GameManager.Instance != null ? GameManager.Instance.Config : null;
            float strength = config != null ? config.paperGrainStrength : 0.04f;
            float scale = config != null ? config.paperGrainScalePixels : 1.5f;

            if (material == null || strength <= 0f)
            {
                Graphics.Blit(source, destination);
                return;
            }

            material.SetFloat(StrengthId, strength);
            material.SetFloat(GrainScaleId, Mathf.Max(scale, 0.01f));
            Graphics.Blit(source, destination, material);
        }

        /// <summary>
        /// Tileable paper texture: white noise softened by a couple of blur
        /// passes, with a slight horizontal bias so it reads as fibres
        /// rather than static. Fixed seed, so every camera and every run
        /// gets the same paper.
        /// </summary>
        private static Texture2D BuildGrain(int size)
        {
            var rng = new System.Random(1234);
            var values = new float[size * size];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = (float)rng.NextDouble();
            }

            // Two passes of a 3-tap blur, wider horizontally than vertically.
            var scratch = new float[values.Length];
            for (int pass = 0; pass < 2; pass++)
            {
                for (int y = 0; y < size; y++)
                {
                    int up = (y + size - 1) % size, down = (y + 1) % size;
                    for (int x = 0; x < size; x++)
                    {
                        int left = (x + size - 1) % size, right = (x + 1) % size;
                        float horizontal = values[y * size + left] + values[y * size + x] * 2f + values[y * size + right];
                        float vertical = values[up * size + x] + values[down * size + x];
                        scratch[y * size + x] = (horizontal * 1.0f + vertical * 0.5f) / 5f;
                    }
                }
                (values, scratch) = (scratch, values);
            }

            // Re-stretch to use the full 0..1 range after blurring.
            float min = float.MaxValue, max = float.MinValue;
            foreach (float v in values) { if (v < min) min = v; if (v > max) max = v; }
            float range = Mathf.Max(max - min, 0.0001f);

            var tex = new Texture2D(size, size, TextureFormat.R8, false, linear: true)
            {
                name = "PaperGrain",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                byte v = (byte)Mathf.RoundToInt((values[i] - min) / range * 255f);
                pixels[i] = new Color32(v, v, v, 255);
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, makeNoLongerReadable: true);
            return tex;
        }
    }
}
