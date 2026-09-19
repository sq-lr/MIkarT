# 0002 — Newtonsoft.Json, not JsonUtility

**Decision:** Deserialize `WorldRecipe` in Unity using
`com.unity.nuget.newtonsoft-json` (added in `unity/Packages/manifest.json`),
not `UnityEngine.JsonUtility`.

**Why:** `JsonUtility` handles nested `List<T>` inconsistently and gives poor
error messages on malformed input — exactly the failure mode we need to
detect and fall back from (`GameManager.OnRecipeFailed` →
`DefaultWorldRecipe`). Newtonsoft is the de facto standard, resolves via one
line in `manifest.json`, and needs no Editor step to add.

**Consequence:** Every teammate's first Editor open must let Package Manager
resolve this dependency before scripts referencing `Newtonsoft.Json` will
compile.
