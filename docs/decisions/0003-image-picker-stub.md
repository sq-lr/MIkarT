# 0003 — Image picker: Editor-only real picker + placeholder fallback

**Decision:** `IImagePicker` (`unity/Assets/Scripts/UI/ImagePicker/`) has two
implementations:
- `EditorFileImagePicker` — a real native file-open dialog via
  `UnityEditor.EditorUtility.OpenFilePanel`, compiled only inside
  `#if UNITY_EDITOR`.
- `PlaceholderImagePicker` — loads a bundled sample image from `Resources/`,
  used in standalone builds.

**Why:** Unity ships no cross-platform runtime file-open dialog. Getting a
real "browse your computer" button in a standalone build requires a
third-party native plugin (e.g. StandaloneFileBrowser), which is an
unlisted, out-of-scope dependency for this bootstrap.

**Consequence:** Testing the real upload flow only works when running inside
the Unity Editor. A hackathon demo built as a standalone player will always
use the bundled placeholder image unless a native picker plugin is added
later — `ImageUploadUI` doesn't need to change when that happens, only the
`picker` implementation swapped in `Awake()`.
