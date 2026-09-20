using System.Collections.Generic;
using MarioKart.Core;
using UnityEngine;

namespace MarioKart.Rendering
{
    /// <summary>
    /// The one place that knows about the MarioKart/Toon shaders
    /// (Assets/Resources/Shaders). Everything that creates a material at
    /// runtime -- the track, placeholders, swapped-in generated meshes --
    /// goes through here, so the whole world shares one cel-shaded look
    /// (docs/decisions/0008).
    ///
    /// Per-world parameters (shadow band tint, rim colour) are shader
    /// globals set once by ApplyPalette, not copied into every material.
    /// If the shaders are missing or GameConfig.toonShading is off, every
    /// call degrades to the Standard shader and the game looks like it did
    /// before -- nothing here can break a race.
    /// </summary>
    public static class ToonStyle
    {
        private const string OutlinedShaderName = "MarioKart/Toon";
        private const string FlatShaderName = "MarioKart/Toon (No Outline)";

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int RimStrengthId = Shader.PropertyToID("_RimStrength");
        private static readonly int ToonBandsId = Shader.PropertyToID("_ToonBands");
        private static readonly int ToonOutlineInkId = Shader.PropertyToID("_ToonOutlineInk");
        private static readonly int ToonDotFadeId = Shader.PropertyToID("_ToonDotFade");
        private static readonly int ToonShadowTintId = Shader.PropertyToID("_ToonShadowTint");
        private static readonly int ToonRimColorId = Shader.PropertyToID("_ToonRimColor");
        private static readonly int ToonRimPowerId = Shader.PropertyToID("_ToonRimPower");
        private static readonly int ToonDotSizeId = Shader.PropertyToID("_ToonDotSize");
        private static readonly int ToonDotStrengthId = Shader.PropertyToID("_ToonDotStrength");

        // glTFast's Built-in "glTF/PbrMetallicRoughness" property names, then
        // the Standard-style names older glTFast versions used.
        private static readonly int[] BaseColorTextureIds =
        {
            Shader.PropertyToID("baseColorTexture"),
            Shader.PropertyToID("_MainTex"),
        };
        private static readonly int[] BaseColorFactorIds =
        {
            Shader.PropertyToID("baseColorFactor"),
            Shader.PropertyToID("_Color"),
        };

        private static Shader outlined, flat, standard;
        private static bool warnedMissing;

        // Shader globals start at zero (black shadow band) until a world
        // applies its palette; give the lobby scene sane values.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyDefaults()
        {
            ApplyPalette(null, new Color(0.70f, 0.75f, 0.82f));
        }

        /// <summary>False when the config turns toon shading off.</summary>
        public static bool Enabled
        {
            get
            {
                var config = GameManager.Instance != null ? GameManager.Instance.Config : null;
                return config == null || config.toonShading;
            }
        }

        /// <summary>Toon shader with the inverted-hull outline pass (Standard if unavailable).</summary>
        public static Shader Outlined => Enabled ? Find(ref outlined, OutlinedShaderName) : Standard;

        /// <summary>Toon shader without an outline, for ground/road/horizon (Standard if unavailable).</summary>
        public static Shader Flat => Enabled ? Find(ref flat, FlatShaderName) : Standard;

        private static Shader Standard => standard != null ? standard : (standard = Shader.Find("Standard"));

        private static Shader Find(ref Shader cache, string name)
        {
            if (cache != null) return cache;
            cache = Shader.Find(name);
            if (cache == null)
            {
                if (!warnedMissing)
                {
                    Debug.LogWarning($"ToonStyle: shader '{name}' not found; falling back to Standard");
                    warnedMissing = true;
                }
                return Standard;
            }
            return cache;
        }

        /// <summary>
        /// A new toon material in `color`, optionally textured. `outline`
        /// false is for flat surfaces (road, ground, far horizon fillers):
        /// they get no outline pass and no rim light -- a fresnel rim on a
        /// plane seen at a grazing angle lights up everything more than a
        /// few metres from the camera, leaving a dark camera-centred blot.
        /// </summary>
        public static Material Create(Color color, bool outline = true, Texture texture = null, string name = null)
        {
            var material = new Material(outline ? Outlined : Flat) { color = color };
            if (texture != null) material.mainTexture = texture;
            if (name != null) material.name = name;
            if (!outline) DisableRim(material);
            return material;
        }

        /// <summary>Turn off the rim light on a toon material (no-op for Standard).</summary>
        public static void DisableRim(Material material)
        {
            if (material != null && material.HasProperty(RimStrengthId))
            {
                material.SetFloat(RimStrengthId, 0f);
            }
        }

        /// <summary>
        /// Rebuild every renderer under `root` (a freshly instantiated glTFast
        /// scene) with toon materials that keep the original base colour and
        /// texture. Any failure leaves the original materials in place.
        /// </summary>
        public static void Restyle(GameObject root)
        {
            if (root == null || !Enabled) return;

            try
            {
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(includeInactive: true))
                {
                    var sources = renderer.sharedMaterials;
                    var replacements = new Material[sources.Length];
                    for (int i = 0; i < sources.Length; i++)
                    {
                        replacements[i] = sources[i] != null ? FromSource(sources[i]) : null;
                    }
                    renderer.sharedMaterials = replacements;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"ToonStyle: could not restyle '{root.name}' ({e.Message}); keeping imported materials");
            }
        }

        private static Material FromSource(Material source)
        {
            Color color = Color.white;
            foreach (int id in BaseColorFactorIds)
            {
                if (source.HasProperty(id)) { color = source.GetColor(id); break; }
            }
            Texture texture = null;
            foreach (int id in BaseColorTextureIds)
            {
                if (source.HasProperty(id)) { texture = source.GetTexture(id); if (texture != null) break; }
            }

            var material = Create(color, true, texture, $"{source.name} (Toon)");
            // Generated meshes carry their own texture detail; a softer rim
            // keeps the outline as the dominant edge cue.
            material.SetFloat(RimStrengthId, 0.35f);
            return material;
        }

        /// <summary>
        /// Crisp cast shadows to match the ink-edged look: hard shadow
        /// filtering on the sun, and a shadow map concentrated on the
        /// distance the chase cameras actually look at (the Ultra quality
        /// preset spreads it over 150 m, which is what makes the kart's
        /// shadow blurry). Objects beyond `distance` cast no shadow, which
        /// only affects the far horizon silhouettes.
        /// </summary>
        public static void ConfigureShadows(Light sun, float distance)
        {
            if (sun != null)
            {
                sun.shadows = LightShadows.Hard;
                // Enough offset that walls, placeholders and karts don't
                // shadow their own lit faces (which the binary shadow
                // threshold in ToonCommon.cginc would turn solid dark), not
                // so much that shadows visibly detach from their casters.
                sun.shadowBias = 0.05f;
                sun.shadowNormalBias = 0.6f;
            }
            QualitySettings.shadowDistance = Mathf.Max(distance, 10f);
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
            QualitySettings.shadowCascades = 2;
        }

        /// <summary>
        /// Set the per-world globals from the recipe palette: the shadow band
        /// takes on the theme's tint (palette[2], the same entry that tints
        /// the walls and sky) pushed cooler, the rim light takes the sun
        /// colour (palette[0]), and ambient becomes a sky/ground gradient
        /// around `skyAmbient`. `skyAmbient` is also the fallback when the
        /// palette is short. Safe to call even when toon shading is off.
        /// </summary>
        public static void ApplyPalette(List<string> palette, Color skyAmbient)
        {
            Color shadowSource = skyAmbient;
            if (palette != null && palette.Count > 2 && ColorUtility.TryParseHtmlString(palette[2], out var tint))
            {
                shadowSource = tint;
            }
            Color.RGBToHSV(shadowSource, out float h, out float s, out float v);
            // Shadows are noticeably darker than the lit band, keep the theme
            // hue rather than going grey, and lean cool: cel animators and
            // painters shift shadows toward blue/violet instead of just
            // darkening, which is what makes the lit side look warm.
            h = ShiftHueToward(h, ShadowHueTarget, ShadowHueShift);
            var shadowTint = Color.HSVToRGB(h, Mathf.Clamp(s + 0.1f, 0.2f, 0.65f), Mathf.Clamp(v, 0.45f, 0.6f));

            Color rim = Color.white;
            if (palette != null && palette.Count > 0 && ColorUtility.TryParseHtmlString(palette[0], out var sun))
            {
                rim = Color.Lerp(sun, Color.white, 0.4f);
            }
            rim.a = 0.6f; // intensity

            // A wide lit band, a narrow mid band, then shadow: equal thirds
            // read as a sphere-shading tutorial, this reads as a drawing.
            Shader.SetGlobalVector(ToonBandsId, new Vector4(0.35f, 0.55f, 0f, 0f));
            Shader.SetGlobalColor(ToonShadowTintId, shadowTint);
            Shader.SetGlobalColor(ToonRimColorId, rim);
            Shader.SetGlobalFloat(ToonRimPowerId, 3f);
            Shader.SetGlobalFloat(ToonOutlineInkId, OutlineInk);

            // Halftone dots (screen-space, so they keep their print size at
            // any distance -- which is why they fade out with depth: on far
            // surfaces they would just be noise). Read from the config when
            // there is one so the Inspector values apply; otherwise defaults.
            var config = GameManager.Instance != null ? GameManager.Instance.Config : null;
            float dotStrength = config != null ? config.toonDotStrength : 0.7f;
            float dotSize = config != null ? config.toonDotSizePixels : 6f;
            Shader.SetGlobalFloat(ToonDotStrengthId, Mathf.Clamp01(dotStrength));
            Shader.SetGlobalFloat(ToonDotSizeId, Mathf.Max(dotSize, 1f));
            Shader.SetGlobalVector(ToonDotFadeId, new Vector4(25f, 60f, 0f, 0f));

            // Ambient as a gradient rather than one flat colour, so tops,
            // sides and undersides in shadow are not all the identical shade.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(skyAmbient, Color.white, 0.15f);
            RenderSettings.ambientEquatorColor = skyAmbient;
            RenderSettings.ambientGroundColor = Color.Lerp(skyAmbient, shadowTint, 0.6f) * 0.8f;
        }

        // Shadow hue: pull toward blue-violet (0.66 on the HSV wheel) by at
        // most ShadowHueShift of a turn, so a warm theme gets cool shadows
        // and a cool theme just gets slightly deeper ones.
        private const float ShadowHueTarget = 0.66f;
        private const float ShadowHueShift = 0.06f;

        // Outline colour: 0 = the object's own colour darkened (painterly),
        // 1 = pure ink (comic). Slightly toward ink keeps outlines readable
        // on the pale walls and horizon fillers.
        private const float OutlineInk = 0.4f;

        private static float ShiftHueToward(float hue, float target, float maxShift)
        {
            float delta = Mathf.Repeat(target - hue + 0.5f, 1f) - 0.5f; // shortest way round the wheel
            return Mathf.Repeat(hue + Mathf.Clamp(delta, -maxShift, maxShift), 1f);
        }
    }
}
