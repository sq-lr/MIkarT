# 0013 — Comic-book lobby screens, built by decorating the baked scene at runtime

**Decision:** The Boot (title / Start) and Upload ("Describe your world")
screens are restyled as comic-book pages -- halftone paper, thick-inked
panel frames with solid drop shadows, the controls hint in a speech
bubble, the title in a jagged burst, tilted onomatopoeia word bursts
("VROOM!", "POW!", ...), a slam-in entrance every time a screen appears,
and a hover wobble on every button and toggle. Three choices shape how:

1. **Restyle at runtime, don't change the scene.** `LobbyUI` and
   `ImageUploadUI` keep every `[SerializeField]` the scene builder wires,
   and in `Awake` a `Decorate()` pass reparents and restyles the baked
   objects in place (the `ResultsUI` pattern): the panel's dim `Image`
   becomes paper, the baked `Title`/`Hint`/`SurfaceHint` texts move into
   new frames, buttons get shadow + face children under their existing
   click target, and toggles become sky-coloured stamps / an inked
   checkbox. `MainSceneBuilder` and `Main.unity` are untouched.
2. **One static kit, every sprite generated in code.** `UI/ComicStyle.cs`
   holds the brand colours, the sprite generators (tileable halftone,
   9-sliced rounded frame, thick select ring, speech-bubble tail, tick,
   and the countdown's spiky burst, lifted from `GenerationUI`), the
   element builders (`AddFrame`, `AddSpeechBubble`, `AddWordBurst`,
   `StyleButton`, `StyleSkyStamp`, `StyleCheckbox`, `StyleInputField`) and
   the motion (`PlaySlams`, `ComicHoverWobble`). Every sprite is white fill
   / ink rim / clear outside, so `Image.color` tints only the fill and the
   rim always stays ink. Sprites are process-lifetime statics: built on
   first use, shared by every screen, released on `Application.quitting`.
3. **Fixed constants for every "hand-drawn" irregularity.** Tilts,
   positions and entrance delays are literals in each screen's layout
   table; nothing consults `UnityEngine.Random` (architecture rule 3's
   habit, even though UI isn't world generation).

**Why runtime decoration rather than the scene builder:** the builder
route means an Editor rebuild and a large `Main.unity` diff for what is
purely a look change, and one rebuild diff was already pending from other
work. Decorating in `Awake` ships with the scene you have, keeps
`MainSceneBuilder`'s field wiring (and its "no serialized field" error
guard) as the only contract, and follows a pattern the codebase already
uses. The cost is that the baked child *names* also become part of that
contract -- documented in `docs/development.md`.

**Why generated sprites rather than assets:** ADR 0005 rules out
hand-authored Unity assets, and the comic look only needs a handful of
simple shapes that a 64² or 256² procedural texture expresses well
(9-slicing keeps the ink line the same thickness at any size). It also
means `GenerationUI` and the lobby share one burst rasteriser instead of
drifting copies.

**Consequences:**

- Baked child names are now a contract; a missing one is skipped with one
  warning and the screen still works, just plainer.
- Sprite textures live for the whole session (a few 64² and up to five
  256² textures); no screen destroys them.
- `GenerationUI` uses `ComicStyle.Burst` / `ComicStyle.EaseOutBack`
  (numerically identical to what it had). `ResultsUI` is untouched.
- The sky toggles' colours now come from the same presets
  `WorldGenerator` applies (sunny / cloudy / sunset / night top + horizon),
  replacing the two divergent colour blocks that existed before.
- Re-picking an image no longer leaks the previous preview texture.
- Generating / HUD / Results keep their existing look; extending the kit
  to them is a separate decision.
