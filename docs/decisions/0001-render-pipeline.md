# 0001 — Built-in Render Pipeline, not URP

**Decision:** Use Unity's Built-in Render Pipeline for this bootstrap.

**Why:** URP requires a pipeline asset and a renderer asset (both Editor-generated
`.asset` files with GUIDs) plus per-material shader reassignment. None of
that can be safely hand-authored without opening the Unity Editor. Built-in
needs zero pipeline asset and works with default `Standard`/`Diffuse`
shaders on primitives out of the box — the fastest path to "open once in the
Editor and it just works."

**Consequence:** The placeholder environment (primitive cubes/spheres/cylinders
from `AssetResolver`) won't have URP-specific lighting features. That's fine —
none of this is final art.

**Revisit when:** The team wants real lit/PBR art and is willing to do the
one-time URP migration (Edit → Render Pipeline → convert materials) inside
the Editor. Note that the game's look is now a Built-in surface shader
(ADR 0008, `Assets/Resources/Shaders/`); a migration would need to port it
to Shader Graph or a URP-style HLSL shader.
