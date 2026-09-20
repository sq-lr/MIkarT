using UnityEngine;

namespace MarioKart.Rendering
{
    /// <summary>
    /// My Neighbor Totoro / Ghibli countryside look: wrap lighting, milky
    /// haze, watercolor sky, puffy clouds. Materials are created at runtime
    /// (ADR 0005 — no hand-authored .mat files). Falls back to Diffuse if
    /// the shaders haven't imported yet.
    /// </summary>
    public static class GhibliLook
    {
        public const string LitShaderName = "MarioKart/GhibliLit";
        public const string SkyShaderName = "MarioKart/GhibliSky";
        public const string PostShaderName = "Hidden/MarioKart/GhibliPost";

        public static readonly Color ShadowGreen = new Color(0.40f, 0.50f, 0.46f);
        public static readonly Color PathDirt = new Color(0.66f, 0.56f, 0.42f);
        public static readonly Color FenceWood = new Color(0.78f, 0.70f, 0.55f);
        public static readonly Color Moss = new Color(0.48f, 0.62f, 0.38f);
        public static readonly Color Cream = new Color(0.93f, 0.90f, 0.80f);
        public static readonly Color Cloud = new Color(0.96f, 0.95f, 0.90f);

        /// <summary>
        /// Pulls a saturated arcade color toward the film's dusty, slightly
        /// faded countryside palette without changing its hue family.
        /// </summary>
        public static Color Countryside(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            s = Mathf.Clamp01(s * 0.62f);
            v = Mathf.Lerp(v, 0.76f, 0.28f);
            var result = Color.HSVToRGB(h, s, v);
            result.a = c.a;
            return result;
        }

        public static Material Lit(Color albedo)
        {
            var shader = Shader.Find(LitShaderName) ?? Shader.Find("Diffuse") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = "GhibliLit", color = albedo };
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", albedo);
            if (mat.HasProperty("_ShadowColor")) mat.SetColor("_ShadowColor", ShadowGreen);
            return mat;
        }

        public static void Restyle(Renderer renderer)
        {
            if (renderer == null || renderer is ParticleSystemRenderer) return;

            // A retrieved/generated mesh often has one material per part
            // (e.g. a bench's "Metal" legs and "Wood" seat as two separate
            // glTF materials) -- restyling only sharedMaterial (submesh 0)
            // left every other submesh on its raw glTFast-imported material,
            // which the render pipeline can show as an error checkerboard.
            // Every submesh needs converting, not just the first.
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = RestyleMaterial(materials[i]);
            }
            renderer.sharedMaterials = materials;
        }

        private static Material RestyleMaterial(Material current)
        {
            Color albedo = Color.white;
            Texture tex = null;
            if (current != null)
            {
                if (current.HasProperty("_Color")) albedo = current.color;
                else if (current.HasProperty("_BaseColor")) albedo = current.GetColor("_BaseColor");
                if (current.HasProperty("_MainTex")) tex = current.GetTexture("_MainTex");
                else if (current.HasProperty("_BaseMap")) tex = current.GetTexture("_BaseMap");
            }

            var mat = Lit(albedo);
            if (tex != null && mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            return mat;
        }

        public static void RestyleTree(GameObject root)
        {
            if (root == null) return;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Restyle(renderer);
            }
        }

        public static void EnsurePostOnCameras()
        {
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (cam.GetComponent<GhibliPostEffect>() == null)
                {
                    cam.gameObject.AddComponent<GhibliPostEffect>();
                }
            }
        }
    }
}
