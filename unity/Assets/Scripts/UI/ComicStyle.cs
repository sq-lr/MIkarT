using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MarioKart.UI
{
    /// <summary>
    /// The comic-book UI kit shared by the lobby screens (and the countdown
    /// burst): brand colours, every sprite the UI needs rasterised in code
    /// (no sprite assets -- see docs/decisions/0005 and 0013), builders that
    /// restyle the scene-baked uGUI controls in place, and the slam-in /
    /// hover-wobble motion. Sprites are process-lifetime statics: generated
    /// on first use, shared by every screen, released when the app quits.
    /// Nothing here is random -- every tilt and delay is a constant the
    /// caller passes in.
    /// </summary>
    public static class ComicStyle
    {
        // ---- design tokens ------------------------------------------------

        public static readonly Color Ink = new Color(20f / 255f, 20f / 255f, 30f / 255f);
        public static readonly Color Paper = new Color(0.93f, 0.90f, 0.80f);
        public static readonly Color White = Color.white;
        public static readonly Color Red = new Color(0.95f, 0.22f, 0.22f);
        public static readonly Color Blue = new Color(0.22f, 0.48f, 0.98f);
        public static readonly Color Yellow = new Color(1f, 0.90f, 0.25f);
        public static readonly Color Orange = new Color(1f, 0.60f, 0.15f);
        public static readonly Color Green = new Color(0.45f, 1f, 0.35f);
        public static readonly Color Gold = new Color(1f, 0.84f, 0.12f);
        public static readonly Color Lime = new Color(0.55f, 0.72f, 0.20f);
        public static readonly Color Disabled = new Color(0.62f, 0.62f, 0.62f);

        /// <summary>Ink line thickness in reference pixels (1920x1080 canvas).</summary>
        public const float RimPx = 6f;
        /// <summary>Offset of the solid-ink drop shadow behind frames and buttons.</summary>
        public const float ShadowOffset = 9f;

        // ---- sprites --------------------------------------------------------

        private const int TileSize = 64;
        private const int BurstSize = 256;
        private const float PixelsPerUnit = 100f;

        private static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        private static bool releaseHooked;

        /// <summary>
        /// Tileable 45-degree Ben-Day dot lattice: white dots, clear
        /// elsewhere, so the Image colour tints the dots. Use with
        /// Image.Type.Tiled; pixelsPerUnitMultiplier scales the dots.
        /// </summary>
        public static Sprite Halftone()
        {
            return Cached("halftone", () =>
            {
                var texture = NewTexture(TileSize, TileSize);
                texture.wrapMode = TextureWrapMode.Repeat;
                var pixels = new Color32[TileSize * TileSize];
                var centres = new[] { new Vector2(16f, 16f), new Vector2(48f, 48f) };
                const float radius = 8.5f;
                for (int y = 0; y < TileSize; y++)
                {
                    for (int x = 0; x < TileSize; x++)
                    {
                        var p = new Vector2(x + 0.5f, y + 0.5f);
                        float nearest = float.MaxValue;
                        // Check the neighbouring tiles too so dots wrap cleanly.
                        foreach (var c in centres)
                        {
                            for (int oy = -1; oy <= 1; oy++)
                            {
                                for (int ox = -1; ox <= 1; ox++)
                                {
                                    nearest = Mathf.Min(nearest, Vector2.Distance(p, c + new Vector2(ox * TileSize, oy * TileSize)));
                                }
                            }
                        }
                        float alpha = Mathf.Clamp01(0.5f + (radius - nearest));
                        pixels[y * TileSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                    }
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                return Sprite.Create(texture, new Rect(0f, 0f, TileSize, TileSize), new Vector2(0.5f, 0.5f), PixelsPerUnit, 0, SpriteMeshType.FullRect);
            });
        }

        /// <summary>Hard-edged spiky star polygon with an ink rim, alpha elsewhere (the countdown starburst).</summary>
        public static Sprite Burst(int points, float innerRadius)
        {
            return Cached($"burst{points}_{innerRadius:0.00}", () =>
            {
                int size = BurstSize;
                var texture = NewTexture(size, size);
                var pixels = new Color32[size * size];
                float half = size * 0.5f;
                var fill = new Color32(255, 255, 255, 255);
                var rim = ToColor32(Ink);
                var clear = new Color32(0, 0, 0, 0);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float angle = Mathf.Atan2(dy, dx);
                        // Edge radius swings between the tips and the notches.
                        float wave = 0.5f + 0.5f * Mathf.Cos(angle * points);
                        float edge = Mathf.Lerp(innerRadius, 0.98f, Mathf.Pow(wave, 1.6f));
                        pixels[y * size + x] = r > edge ? clear : r > edge - 0.045f ? rim : fill;
                    }
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), PixelsPerUnit);
            });
        }

        /// <summary>
        /// Rounded rectangle with an ink rim, 9-sliced so the rim stays
        /// RimPx thick at any size. White fill (tint via Image.color);
        /// tinted Ink it doubles as the drop-shadow block.
        /// </summary>
        public static Sprite Frame() => Cached("frame", () => RoundedFrame(cornerPx: 14f, rimPx: RimPx, hollow: false, border: 20f));

        /// <summary>Thick hollow ring, same corner radius as Frame -- the "selected" overlay on toggles.</summary>
        public static Sprite SelectRing() => Cached("ring", () => RoundedFrame(cornerPx: 14f, rimPx: 11f, hollow: true, border: 24f));

        /// <summary>
        /// Speech-bubble tail: a triangle with its base along the top edge
        /// and the tip near bottom-left. Only the two slanted edges get the
        /// ink rim, and the top band is plain white so it can overlap the
        /// bubble body's rim and read as one shape.
        /// </summary>
        public static Sprite BubbleTail()
        {
            return Cached("tail", () =>
            {
                int size = TileSize;
                var texture = NewTexture(size, size);
                var pixels = new Color32[size * size];
                var a = new Vector2(6f, size);
                var b = new Vector2(size - 6f, size);
                var tip = new Vector2(18f, 4f);
                const float overlapBand = 8f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        var p = new Vector2(x + 0.5f, y + 0.5f);
                        // Signed distance inside the triangle (positive inside).
                        float inside = Mathf.Min(SignedEdge(p, a, tip), Mathf.Min(SignedEdge(p, tip, b), SignedEdge(p, b, a)));
                        float alpha = Mathf.Clamp01(0.5f + inside);
                        float edgeDist = Mathf.Min(SegmentDistance(p, a, tip), SegmentDistance(p, b, tip));
                        bool inkBand = p.y < size - overlapBand && edgeDist < RimPx;
                        var colour = inkBand ? Ink : White;
                        pixels[y * size + x] = new Color32((byte)(colour.r * 255f), (byte)(colour.g * 255f), (byte)(colour.b * 255f), (byte)(alpha * 255f));
                    }
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), PixelsPerUnit);
            });
        }

        /// <summary>A hand-drawn tick, white (tint to Ink), so no font glyph is needed.</summary>
        public static Sprite Check()
        {
            return Cached("check", () =>
            {
                int size = TileSize;
                var texture = NewTexture(size, size);
                var pixels = new Color32[size * size];
                var p0 = new Vector2(12f, 30f);
                var p1 = new Vector2(26f, 16f);
                var p2 = new Vector2(52f, 48f);
                const float halfThickness = 4f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        var p = new Vector2(x + 0.5f, y + 0.5f);
                        float d = Mathf.Min(SegmentDistance(p, p0, p1), SegmentDistance(p, p1, p2));
                        float alpha = Mathf.Clamp01(0.5f + (halfThickness - d));
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                    }
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), PixelsPerUnit);
            });
        }

        private static Sprite RoundedFrame(float cornerPx, float rimPx, bool hollow, float border)
        {
            int size = TileSize;
            var texture = NewTexture(size, size);
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            // One pixel of margin so the anti-aliased outer edge is never clipped.
            var boxHalf = new Vector2(half - 1f, half - 1f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f - half, y + 0.5f - half);
                    float d = RoundedBoxDistance(p, boxHalf, cornerPx);
                    float outer = Mathf.Clamp01(0.5f - d);            // 1 inside the shape
                    float fill = Mathf.Clamp01(0.5f - (d + rimPx));   // 1 inside the fill (past the rim)
                    Color colour;
                    float alpha;
                    if (hollow)
                    {
                        // A tintable white band with a thin ink line on both edges.
                        const float edgePx = 2f;
                        bool edge = d > -edgePx || d < -rimPx + edgePx;
                        colour = edge ? Ink : White;
                        alpha = outer * (1f - fill);
                    }
                    else
                    {
                        colour = Color.Lerp(Ink, White, fill);
                        alpha = outer;
                    }
                    pixels[y * size + x] = new Color32((byte)(colour.r * 255f), (byte)(colour.g * 255f), (byte)(colour.b * 255f), (byte)(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), PixelsPerUnit, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        private static float RoundedBoxDistance(Vector2 p, Vector2 halfSize, float radius)
        {
            var q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - halfSize + Vector2.one * radius;
            var clipped = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f));
            return clipped.magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }

        /// <summary>Signed distance from p to the line through a->b; positive on the left (inside a counter-clockwise polygon).</summary>
        private static float SignedEdge(Vector2 p, Vector2 a, Vector2 b)
        {
            var e = b - a;
            float len = e.magnitude;
            if (len < 1e-5f) return 0f;
            return (e.x * (p.y - a.y) - e.y * (p.x - a.x)) / len;
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        private static Texture2D NewTexture(int width, int height)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
        }

        private static Color32 ToColor32(Color c) => new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(c.a * 255f));

        private static Sprite Cached(string key, System.Func<Sprite> make)
        {
            if (sprites.TryGetValue(key, out var existing) && existing != null) return existing;
            if (!releaseHooked)
            {
                Application.quitting += Release;
                releaseHooked = true;
            }
            var sprite = make();
            sprite.hideFlags = HideFlags.DontSave;
            sprites[key] = sprite;
            return sprite;
        }

        private static void Release()
        {
            foreach (var sprite in sprites.Values)
            {
                if (sprite == null) continue;
                Object.Destroy(sprite.texture);
                Object.Destroy(sprite);
            }
            sprites.Clear();
        }

        // Enter Play Mode without domain reload keeps statics: forget the dead
        // sprites from the previous run so they get rebuilt.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            sprites.Clear();
            releaseHooked = false;
        }

        // ---- easing -----------------------------------------------------------

        /// <summary>
        /// Back-easing: shoots past 1 and springs back. <paramref name="s"/>
        /// is the classic overshoot constant (1.7 = ResultsUI's stamps).
        /// </summary>
        public static float EaseOutBack(float t, float s = 1.7f)
        {
            t -= 1f;
            return t * t * ((s + 1f) * t + s) + 1f;
        }

        /// <summary>GenerationUI's "how far past full size" tunable mapped onto EaseOutBack's constant.</summary>
        public static float BackStrength(float overshoot) => (overshoot - 1f) * 3.5f + 1.7f;

        // ---- rect helpers -------------------------------------------------------

        public static RectTransform CreateUi(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        public static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        /// <summary>Centre-anchored placement with an optional lean, in reference pixels.</summary>
        public static void Place(RectTransform rect, Vector2 position, Vector2 size, float tilt = 0f)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localRotation = Quaternion.Euler(0f, 0f, tilt);
        }

        public static Image MakeImage(string name, Transform parent, Sprite sprite, Color tint, Image.Type type)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = tint;
            image.type = type;
            image.raycastTarget = false;
            return image;
        }

        // ---- text ------------------------------------------------------------

        /// <summary>Comic lettering: white Bangers gets an ink outline and a light shadow, white body copy a hairline outline, and dark text is left plain.</summary>
        public static Text MakeLabel(string name, Transform parent, string copy, int size, Color color, bool body = false, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = copy;
            text.alignment = align;
            RestyleLabel(text, size, color, body);
            return text;
        }

        /// <summary>Apply the comic lettering to an existing (scene-baked) Text in place.</summary>
        public static void RestyleLabel(Text text, int size, Color color, bool body = false)
        {
            if (text == null) return;
            text.font = body ? GameFonts.Body : GameFonts.Display;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = FontStyle.Normal;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            // Dark lettering (ink on paper) is left plain: an ink outline or
            // shadow under it only smears. Light lettering gets the comic
            // treatment -- a solid ink outline, plus a small faint shadow on
            // display text; body copy gets just a hairline outline.
            bool dark = 0.299f * color.r + 0.587f * color.g + 0.114f * color.b < 0.4f;
            var outline = text.GetComponent<Outline>();
            Shadow shadow = null;
            foreach (var candidate in text.GetComponents<Shadow>())
            {
                if (!(candidate is Outline)) { shadow = candidate; break; } // Outline derives from Shadow
            }

            if (dark)
            {
                if (outline != null) Object.Destroy(outline);
                if (shadow != null) Object.Destroy(shadow);
                return;
            }

            float outlinePx = body ? Mathf.Clamp(size / 24f, 1f, 2f) : Mathf.Clamp(size / 16f, 2f, 5f);
            if (outline == null) outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(Ink.r, Ink.g, Ink.b, body ? 0.6f : 0.9f);
            outline.effectDistance = new Vector2(outlinePx, -outlinePx);
            outline.useGraphicAlpha = true;

            if (body)
            {
                if (shadow != null) Object.Destroy(shadow);
                return;
            }
            if (shadow == null) shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(Ink.r, Ink.g, Ink.b, 0.22f);
            float shadowPx = Mathf.Clamp(size / 20f, 2f, 5f);
            shadow.effectDistance = new Vector2(shadowPx, -shadowPx);
            shadow.useGraphicAlpha = true;
        }

        // ---- composite elements -----------------------------------------------------

        /// <summary>
        /// Turns the panel's flat dim Image into opaque paper and lays a
        /// tiled halftone over it (first sibling, so everything else draws
        /// on top). Returns the halftone rect.
        /// </summary>
        public static RectTransform AddHalftoneBackdrop(GameObject panel, Color paper, Color dots, float dotScale = 1f)
        {
            var backdrop = panel.GetComponent<Image>();
            if (backdrop == null) backdrop = panel.AddComponent<Image>();
            backdrop.sprite = null;
            backdrop.color = new Color(paper.r, paper.g, paper.b, 1f);
            backdrop.raycastTarget = true;

            var halftone = MakeImage("Halftone", panel.transform, Halftone(), dots, Image.Type.Tiled);
            halftone.pixelsPerUnitMultiplier = 1f / Mathf.Max(0.05f, dotScale);
            Stretch(halftone.rectTransform);
            halftone.transform.SetAsFirstSibling();
            return halftone.rectTransform;
        }

        /// <summary>
        /// An inked comic panel: Root (tilted) > Shadow (ink block, offset)
        /// > Face (fill). Reparent content under the returned Root; the
        /// Root is what the slam-in animates.
        /// </summary>
        public static RectTransform AddFrame(Transform parent, string name, Vector2 position, Vector2 size, Color fill, float tilt = 0f, bool shadow = true)
        {
            var root = CreateUi(name, parent);
            Place(root, position, size, tilt);
            if (shadow)
            {
                var block = MakeImage("Shadow", root, Frame(), Ink, Image.Type.Sliced);
                Stretch(block.rectTransform);
                block.rectTransform.anchoredPosition = new Vector2(ShadowOffset, -ShadowOffset);
            }
            var face = MakeImage("Face", root, Frame(), fill, Image.Type.Sliced);
            Stretch(face.rectTransform);
            return root;
        }

        /// <summary>The yellow caption strip of a comic page; same structure as AddFrame.</summary>
        public static RectTransform AddCaptionBox(Transform parent, string name, Vector2 position, Vector2 size, Color fill, float tilt = 0f)
        {
            return AddFrame(parent, name, position, size, fill, tilt, shadow: true);
        }

        /// <summary>
        /// A frame with a tail. <paramref name="tailAnchor01"/> is where on
        /// the bubble's edge the tail sits (e.g. (0.16, 1) = top edge, left
        /// of centre); <paramref name="tailAngle"/> rotates it -- 0 hangs
        /// off the bottom edge, 180 points up off the top edge.
        /// </summary>
        public static RectTransform AddSpeechBubble(Transform parent, string name, Vector2 position, Vector2 size, Vector2 tailAnchor01, float tailAngle, Color fill)
        {
            var root = AddFrame(parent, name, position, size, fill, 0f, shadow: true);
            var face = root.Find("Face");
            const float tailSize = 56f;
            const float overlap = 7f; // the tail's plain top band, scaled from 8 of 64 texture pixels
            var inward = Quaternion.Euler(0f, 0f, tailAngle) * new Vector3(0f, overlap, 0f);

            var shadowTail = MakeImage("ShadowTail", root, BubbleTail(), Ink, Image.Type.Simple);
            PlaceTail(shadowTail.rectTransform, tailAnchor01, (Vector2)inward + new Vector2(ShadowOffset, -ShadowOffset), tailAngle, tailSize);
            shadowTail.transform.SetSiblingIndex(face != null ? face.GetSiblingIndex() : 0);

            var tail = MakeImage("Tail", root, BubbleTail(), fill, Image.Type.Simple);
            PlaceTail(tail.rectTransform, tailAnchor01, inward, tailAngle, tailSize);
            tail.transform.SetAsLastSibling();
            return root;
        }

        private static void PlaceTail(RectTransform rect, Vector2 anchor01, Vector2 offset, float angle, float size)
        {
            rect.anchorMin = rect.anchorMax = anchor01;
            rect.pivot = new Vector2(0.5f, 1f); // the base of the triangle
            rect.anchoredPosition = offset;
            rect.sizeDelta = new Vector2(size, size);
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>A tilted onomatopoeia burst ("VROOM!") with lettering on top. Returns the Root.</summary>
        public static RectTransform AddWordBurst(Transform parent, string word, Vector2 position, Vector2 size, float tilt, Color fill, int points = 12, float inner = 0.6f, int fontSize = 52, Color? textColor = null)
        {
            var root = CreateUi($"WordBurst_{word.TrimEnd('!')}", parent);
            Place(root, position, size, tilt);
            var burst = MakeImage("Burst", root, Burst(points, inner), fill, Image.Type.Simple);
            burst.preserveAspect = false;
            Stretch(burst.rectTransform);
            var label = MakeLabel("Label", root, word, fontSize, textColor ?? White);
            Stretch(label.rectTransform);
            return root;
        }

        // ---- control restyling --------------------------------------------------------

        /// <summary>
        /// Sticker-style button: an ink shadow block, a coloured inked face
        /// and white Bangers lettering, all children of the baked button so
        /// the slam and wobble move them together. The baked root Image is
        /// kept (cleared) as the click target.
        /// </summary>
        public static void StyleButton(Button button, Color fill, string label = null, int fontSize = 40)
        {
            if (button == null) return;
            var root = button.GetComponent<Image>();
            if (root == null) root = button.gameObject.AddComponent<Image>();
            root.sprite = null;
            root.color = Color.clear;
            root.raycastTarget = true;

            var face = BuildShadowedFace(button.transform, fill);
            button.targetGraphic = face;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = StickerColors();

            var text = button.transform.Find("Label")?.GetComponent<Text>();
            if (text == null) text = MakeLabel("Label", button.transform, label ?? "", fontSize, White);
            if (label != null) text.text = label;
            RestyleLabel(text, fontSize, White);
            Stretch(text.rectTransform);
            text.alignment = TextAnchor.MiddleCenter;
            text.transform.SetAsLastSibling();

            var wobble = AddHoverWobble(button);
            wobble.WatchFace(face, fill);
        }

        private static Image BuildShadowedFace(Transform parent, Color fill)
        {
            var shadow = MakeImage("Shadow", parent, Frame(), Ink, Image.Type.Sliced);
            Stretch(shadow.rectTransform);
            shadow.rectTransform.anchoredPosition = new Vector2(ShadowOffset, -ShadowOffset);
            shadow.transform.SetSiblingIndex(0);

            var face = MakeImage("Face", parent, Frame(), fill, Image.Type.Sliced);
            Stretch(face.rectTransform);
            face.transform.SetSiblingIndex(1);
            return face;
        }

        private static ColorBlock StickerColors()
        {
            return new ColorBlock
            {
                normalColor = White,
                highlightedColor = White, // the wobble is the hover feedback
                pressedColor = new Color(0.82f, 0.82f, 0.82f),
                selectedColor = White,
                disabledColor = White,    // the face swaps to Disabled grey instead of multiplying
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };
        }

        /// <summary>
        /// A sky toggle as a tilted postage stamp: the frame is the sky
        /// colour, a lower band is the horizon colour, and a thick yellow
        /// ring appears when selected.
        /// </summary>
        public static void StyleSkyStamp(Toggle toggle, Color skyTop, Color skyHorizon, string label, float tilt)
        {
            if (toggle == null) return;
            var rect = toggle.GetComponent<RectTransform>();
            rect.localRotation = Quaternion.Euler(0f, 0f, tilt);

            var frame = toggle.GetComponent<Image>();
            if (frame == null) frame = toggle.gameObject.AddComponent<Image>();
            frame.sprite = Frame();
            frame.type = Image.Type.Sliced;
            frame.color = skyTop;
            frame.raycastTarget = true;
            toggle.targetGraphic = frame;
            toggle.transition = Selectable.Transition.ColorTint;
            toggle.colors = StickerColors();

            var horizon = MakeImage("Horizon", toggle.transform, Frame(), skyHorizon, Image.Type.Sliced);
            horizon.rectTransform.anchorMin = new Vector2(0f, 0f);
            horizon.rectTransform.anchorMax = new Vector2(1f, 0.46f);
            horizon.rectTransform.offsetMin = new Vector2(3f, 3f);
            horizon.rectTransform.offsetMax = new Vector2(-3f, 0f);
            horizon.transform.SetSiblingIndex(0);

            var ring = MakeImage("Selected", toggle.transform, SelectRing(), Yellow, Image.Type.Sliced);
            Stretch(ring.rectTransform, -3f);
            toggle.graphic = ring;
            ring.canvasRenderer.SetAlpha(toggle.isOn ? 1f : 0f);

            var text = toggle.transform.Find("Label")?.GetComponent<Text>();
            if (text == null) text = MakeLabel("Label", toggle.transform, label, 26, White);
            text.text = label;
            RestyleLabel(text, 26, White);
            text.alignment = TextAnchor.MiddleCenter;
            text.rectTransform.anchorMin = new Vector2(0f, 0f);
            text.rectTransform.anchorMax = new Vector2(1f, 0.46f);
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            text.transform.SetAsLastSibling();

            AddHoverWobble(toggle, hoverScale: 1.08f, hoverTilt: 3f);
        }

        /// <summary>An inked checkbox on a paper strip; the baked Checkmark Image becomes the tick.</summary>
        public static void StyleCheckbox(Toggle toggle, string label = null)
        {
            if (toggle == null) return;
            var strip = toggle.GetComponent<Image>();
            if (strip == null) strip = toggle.gameObject.AddComponent<Image>();
            strip.sprite = Frame();
            strip.type = Image.Type.Sliced;
            strip.color = Paper;
            strip.raycastTarget = true;

            var box = MakeImage("Box", toggle.transform, Frame(), White, Image.Type.Sliced);
            box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            box.rectTransform.pivot = new Vector2(0f, 0.5f);
            box.rectTransform.anchoredPosition = new Vector2(8f, 0f);
            box.rectTransform.sizeDelta = new Vector2(40f, 40f);
            box.transform.SetSiblingIndex(0);
            toggle.targetGraphic = box;
            toggle.transition = Selectable.Transition.ColorTint;
            var colors = StickerColors();
            colors.highlightedColor = new Color(0.94f, 0.94f, 0.90f);
            toggle.colors = colors;

            var check = toggle.transform.Find("Checkmark")?.GetComponent<Image>();
            if (check == null) check = MakeImage("Checkmark", toggle.transform, Check(), Ink, Image.Type.Simple);
            check.sprite = Check();
            check.type = Image.Type.Simple;
            check.color = Ink;
            check.raycastTarget = false;
            check.rectTransform.anchorMin = check.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            check.rectTransform.pivot = new Vector2(0f, 0.5f);
            check.rectTransform.anchoredPosition = new Vector2(11f, 0f);
            check.rectTransform.sizeDelta = new Vector2(34f, 34f);
            check.transform.SetAsLastSibling();
            toggle.graphic = check;
            check.canvasRenderer.SetAlpha(toggle.isOn ? 1f : 0f);

            var text = toggle.transform.Find("Label")?.GetComponent<Text>();
            if (text == null) text = MakeLabel("Label", toggle.transform, label ?? "", 24, Ink, body: true, align: TextAnchor.MiddleLeft);
            if (label != null) text.text = label;
            RestyleLabel(text, 24, Ink, body: true);
            text.alignment = TextAnchor.MiddleLeft;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(58f, 0f);
            text.rectTransform.offsetMax = Vector2.zero;

            AddHoverWobble(toggle, hoverScale: 1.02f, hoverTilt: 0f);
        }

        /// <summary>White inked box, ink lettering and caret, yellow selection.</summary>
        public static void StyleInputField(InputField field)
        {
            if (field == null) return;
            var box = field.GetComponent<Image>();
            if (box == null) box = field.gameObject.AddComponent<Image>();
            box.sprite = Frame();
            box.type = Image.Type.Sliced;
            box.color = White;
            box.raycastTarget = true;
            field.targetGraphic = box;
            field.transition = Selectable.Transition.ColorTint;
            var colors = StickerColors();
            colors.highlightedColor = colors.selectedColor = new Color(1f, 0.98f, 0.88f);
            field.colors = colors;

            if (field.textComponent != null)
            {
                var text = field.textComponent;
                text.font = GameFonts.Body;
                text.fontSize = 28;
                text.color = Ink;
                text.fontStyle = FontStyle.Normal;
                Stretch(text.rectTransform, 16f);
            }
            var placeholder = field.placeholder as Text;
            if (placeholder != null)
            {
                placeholder.font = GameFonts.Body;
                placeholder.fontSize = 28;
                placeholder.color = new Color(Ink.r, Ink.g, Ink.b, 0.45f);
                placeholder.fontStyle = FontStyle.Italic;
                Stretch(placeholder.rectTransform, 16f);
            }
            field.customCaretColor = true;
            field.caretColor = Ink;
            field.caretWidth = 3;
            field.selectionColor = new Color(Yellow.r, Yellow.g, Yellow.b, 0.55f);
        }

        // ---- motion -------------------------------------------------------------------

        /// <summary>One element's slam-in: when it starts, how long it takes, the lean it settles into, and a sound on impact.</summary>
        public struct Slam
        {
            public RectTransform rect;
            public float delay;
            public float duration;
            public float lean;
            public float overshoot;
            public string sound;
            public float volume;
            public float pitch;

            public Slam(RectTransform rect, float delay, float duration, float lean, float overshoot = 1.35f, string sound = null, float volume = 0.5f, float pitch = 1f)
            {
                this.rect = rect;
                this.delay = delay;
                this.duration = duration;
                this.lean = lean;
                this.overshoot = overshoot;
                this.sound = sound;
                this.volume = volume;
                this.pitch = pitch;
            }
        }

        /// <summary>
        /// Plays every slam from one unscaled clock. Everything is shrunk to
        /// nothing before the first frame renders (no flash of the final
        /// layout), then each element pops in with the countdown's
        /// overshoot-and-settle feel; hover wobbles are paused until their
        /// element has landed.
        /// </summary>
        public static IEnumerator PlaySlams(IList<Slam> slams)
        {
            foreach (var slam in slams)
            {
                if (slam.rect == null) continue;
                slam.rect.localScale = Vector3.zero;
                var wobble = slam.rect.GetComponent<ComicHoverWobble>();
                if (wobble != null) wobble.enabled = false;
            }

            var landed = new bool[slams.Count];
            var sounded = new bool[slams.Count];
            int remaining = 0;
            for (int i = 0; i < slams.Count; i++) if (slams[i].rect != null) remaining++;

            float clock = 0f;
            while (remaining > 0)
            {
                yield return null;
                clock += Time.unscaledDeltaTime;
                for (int i = 0; i < slams.Count; i++)
                {
                    var slam = slams[i];
                    if (landed[i] || slam.rect == null) continue;
                    if (clock < slam.delay) continue;
                    if (!sounded[i])
                    {
                        sounded[i] = true;
                        if (!string.IsNullOrEmpty(slam.sound)) UISounds.Play(slam.sound, slam.volume, slam.pitch);
                    }
                    float k = Mathf.Clamp01((clock - slam.delay) / Mathf.Max(0.01f, slam.duration));
                    float scale = Mathf.LerpUnclamped(0.15f, 1f, EaseOutBack(k, BackStrength(slam.overshoot)));
                    float tilt = Mathf.LerpUnclamped(slam.lean * 2.5f, slam.lean, EaseOutBack(k, 1.2f));
                    slam.rect.localScale = Vector3.one * scale;
                    slam.rect.localRotation = Quaternion.Euler(0f, 0f, tilt);
                    if (k >= 1f)
                    {
                        landed[i] = true;
                        remaining--;
                        Land(slam);
                    }
                }
            }
        }

        /// <summary>Jump every element to its resting pose (used when a screen hides mid-entrance).</summary>
        public static void SnapToRest(IList<Slam> slams)
        {
            foreach (var slam in slams)
            {
                if (slam.rect == null) continue;
                Land(slam);
            }
        }

        private static void Land(Slam slam)
        {
            slam.rect.localScale = Vector3.one;
            slam.rect.localRotation = Quaternion.Euler(0f, 0f, slam.lean);
            var wobble = slam.rect.GetComponent<ComicHoverWobble>();
            if (wobble != null)
            {
                wobble.SetRest(1f, slam.lean);
                wobble.enabled = true;
            }
        }

        /// <summary>Give a button or toggle a hover scale-up with a little shake. Safe to call twice.</summary>
        public static ComicHoverWobble AddHoverWobble(Selectable control, float hoverScale = 1.06f, float hoverTilt = -2f)
        {
            var wobble = control.GetComponent<ComicHoverWobble>();
            if (wobble == null) wobble = control.gameObject.AddComponent<ComicHoverWobble>();
            wobble.hoverScale = hoverScale;
            wobble.hoverTilt = hoverTilt;
            wobble.SetRest(control.transform.localScale.x, control.transform.localEulerAngles.z);
            return wobble;
        }
    }

    /// <summary>
    /// Hover feedback for comic controls: the element grows a touch and
    /// gives a quick decaying shake, shrinks while pressed, and springs
    /// back on exit. Only reacts while the control is interactable, like
    /// UISoundListener. Optionally swaps a face Image between its fill and
    /// the Disabled grey so a disabled button reads as inert.
    /// </summary>
    [RequireComponent(typeof(Selectable))]
    public class ComicHoverWobble : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public float hoverScale = 1.06f;
        public float hoverTilt = -2f;

        private Selectable control;
        private RectTransform rect;
        private float restScale = 1f;
        private float restTilt;
        private bool hovered;
        private bool pressed;
        private float wobbleTime;
        private Image face;
        private Color faceFill;

        private void Awake()
        {
            control = GetComponent<Selectable>();
            rect = GetComponent<RectTransform>();
        }

        private void OnDisable()
        {
            hovered = false;
            pressed = false;
        }

        public void SetRest(float scale, float tilt)
        {
            restScale = scale;
            restTilt = tilt > 180f ? tilt - 360f : tilt;
        }

        public void WatchFace(Image faceImage, Color fill)
        {
            face = faceImage;
            faceFill = fill;
        }

        private bool Active => control != null && control.IsInteractable();

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!Active) return;
            hovered = true;
            wobbleTime = 0f;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            pressed = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Active) pressed = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            pressed = false;
        }

        private void Update()
        {
            if (rect == null) return;
            bool active = Active;
            if (face != null) face.color = active ? faceFill : ComicStyle.Disabled;
            if (!active) { hovered = false; pressed = false; }

            float targetScale = pressed ? restScale * 0.94f : hovered ? restScale * hoverScale : restScale;
            float shake = hovered ? 5f * Mathf.Sin(wobbleTime * 28f) * Mathf.Exp(-wobbleTime * 7f) : 0f;
            float targetTilt = restTilt + (hovered ? hoverTilt + shake : 0f);

            float dt = Time.unscaledDeltaTime;
            float blend = 1f - Mathf.Exp(-18f * dt);
            float scale = Mathf.Lerp(rect.localScale.x, targetScale, blend);
            float current = rect.localEulerAngles.z;
            if (current > 180f) current -= 360f;
            float tilt = Mathf.Lerp(current, targetTilt, blend);
            rect.localScale = Vector3.one * scale;
            rect.localRotation = Quaternion.Euler(0f, 0f, tilt);
            wobbleTime += dt;
        }
    }
}
