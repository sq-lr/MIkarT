# 0005 — No hand-authored scenes, prefabs, or .meta files

**Decision:** This bootstrap commits every `.cs` script, the folder
structure, `Packages/manifest.json`, and `ProjectSettings/ProjectVersion.txt`,
but does **not** commit any `.unity` scene, `.prefab`, `ScriptableObject`
`.asset`, or `.meta` file.

**Why:** Unity's asset database allocates and cross-references GUIDs for
these file types via the Editor. Hand-inventing GUIDs (or omitting `.meta`
files that the Editor expects) risks broken or ambiguous object references
that silently corrupt the project — a failure mode that's hard to detect and
worse than just not having the files yet.

**Consequence:** `docs/development.md` contains a one-time "open in the
Editor and wire it up" checklist that a human must run before the game is
playable. This is the single unavoidable manual step in this bootstrap.
`GameConfig` and `PlayerInputConfig` were deliberately made plain
serializable C# classes rather than `ScriptableObject`s specifically to avoid
needing any `.asset` file for them to be Inspector-tunable.
