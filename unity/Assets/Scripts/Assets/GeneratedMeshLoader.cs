using System.Collections;
using System.Collections.Generic;
using GLTFast;
using MarioKart.AI;
using MarioKart.Core;
using UnityEngine;

namespace MarioKart.AssetsSystem
{
    /// <summary>
    /// Swaps backend-generated meshes in over primitive placeholders
    /// (environment props and track pickups). For every object type that
    /// carries a mesh task: poll GET /assets/{task_id} until ready (or
    /// failed / timed out), download the GLB, import it once with glTFast,
    /// then place one copy at every placeholder of that type and hide the
    /// placeholder's renderer.
    ///
    /// Placeholder transforms (deterministic from the recipe seed) are never
    /// moved -- the mesh copies just adopt them -- so determinism holds with
    /// or without meshes. The race never waits on this: it starts on
    /// placeholders and any failure simply leaves them in place.
    ///
    /// Lives on the same GameObject as EnvironmentGenerator (or one that
    /// isn't a child of it, since Generate() destroys its children).
    /// </summary>
    public class GeneratedMeshLoader : MonoBehaviour
    {
        [SerializeField] private MeshAssetClient client;

        private readonly AssetCache cache = new AssetCache();
        private readonly List<GltfImport> imports = new List<GltfImport>();
        private readonly Dictionary<string, GameObject> templatesByObjectType = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, List<GameObject>> pendingByType = new Dictionary<string, List<GameObject>>();
        private readonly HashSet<string> loadingTypes = new HashSet<string>();
        private Transform templateRoot;

        /// <summary>
        /// Cancel in-flight loads and drop imported templates. Called by
        /// EnvironmentGenerator before it rebuilds the environment.
        /// </summary>
        public void Clear()
        {
            StopAllCoroutines();
            cache.ClearMeshTemplates();
            templatesByObjectType.Clear();
            pendingByType.Clear();
            loadingTypes.Clear();
            foreach (var import in imports)
            {
                import.Dispose();
            }
            imports.Clear();
            if (templateRoot != null)
            {
                Destroy(templateRoot.gameObject);
                templateRoot = null;
            }
        }

        /// <summary>
        /// Start loading every generated mesh referenced by `definitions`.
        /// `placeholdersByType` maps object type -> the placeholder instances
        /// spawned for it. Safe to call again later (e.g. track obstacles
        /// after environment) — already-ready templates swap immediately,
        /// in-flight loads pick up extra placeholders.
        /// </summary>
        public void Begin(IEnumerable<AssetDefinition> definitions, Dictionary<string, List<GameObject>> placeholdersByType)
        {
            if (client == null)
            {
                Debug.LogWarning("GeneratedMeshLoader: no MeshAssetClient assigned; keeping placeholders");
                return;
            }

            var config = GameManager.Instance != null ? GameManager.Instance.Config : new GameConfig();

            foreach (var definition in definitions)
            {
                if (!definition.HasGeneratedMesh) continue;
                if (placeholdersByType != null &&
                    placeholdersByType.TryGetValue(definition.objectType, out var placeholders))
                {
                    foreach (var placeholder in placeholders)
                    {
                        AttachPlaceholder(definition.objectType, placeholder);
                    }
                }

                if (templatesByObjectType.ContainsKey(definition.objectType)) continue;
                if (!loadingTypes.Add(definition.objectType)) continue;
                StartCoroutine(LoadAndSwap(definition, config));
            }
        }

        /// <summary>
        /// Swap a generated mesh onto a newly spawned placeholder (used when
        /// a collected obstacle respawns). No-op until that type's mesh is
        /// ready; if a load is already in flight the placeholder joins it.
        /// </summary>
        public void AttachPlaceholder(string objectType, GameObject placeholder)
        {
            if (string.IsNullOrEmpty(objectType) || placeholder == null) return;

            if (templatesByObjectType.TryGetValue(objectType, out var template) && template != null)
            {
                PlaceOver(template, placeholder);
                return;
            }

            if (!pendingByType.TryGetValue(objectType, out var pending))
            {
                pending = new List<GameObject>();
                pendingByType[objectType] = pending;
            }
            pending.Add(placeholder);
        }

        private IEnumerator LoadAndSwap(AssetDefinition definition, GameConfig config)
        {
            string taskId = definition.meshTaskId;

            // 1. Poll until the backend reports the mesh ready.
            float deadline = Time.realtimeSinceStartup + config.assetPollTimeoutSeconds;
            while (true)
            {
                MeshTaskStatus status = null;
                string error = null;
                bool done = false;
                client.PollStatus(taskId, s => { status = s; done = true; }, e => { error = e; done = true; });
                yield return new WaitUntil(() => done);

                if (status != null && status.status == MeshTaskStatus.Ready) break;

                if (status != null && status.status == MeshTaskStatus.Failed)
                {
                    Debug.LogWarning($"GeneratedMeshLoader: mesh for '{definition.objectType}' failed ({status.error}); keeping placeholder");
                    loadingTypes.Remove(definition.objectType);
                    yield break;
                }
                if (error != null)
                {
                    // Backend unreachable or bad response: keep polling until
                    // the timeout rather than giving up on the first hiccup.
                    Debug.Log($"GeneratedMeshLoader: status poll for '{definition.objectType}' errored ({error}); retrying");
                }
                if (Time.realtimeSinceStartup > deadline)
                {
                    Debug.LogWarning($"GeneratedMeshLoader: timed out waiting for mesh '{definition.objectType}'; keeping placeholder");
                    loadingTypes.Remove(definition.objectType);
                    yield break;
                }

                yield return new WaitForSecondsRealtime(config.assetPollIntervalSeconds);
            }

            // 2. Download + import once per task.
            if (!cache.TryGetMeshTemplate(taskId, out var template))
            {
                byte[] glb = null;
                string downloadError = null;
                bool downloaded = false;
                client.DownloadModel(taskId, b => { glb = b; downloaded = true; }, e => { downloadError = e; downloaded = true; });
                yield return new WaitUntil(() => downloaded);

                if (glb == null || glb.Length == 0)
                {
                    Debug.LogWarning($"GeneratedMeshLoader: download failed for '{definition.objectType}' ({downloadError}); keeping placeholder");
                    loadingTypes.Remove(definition.objectType);
                    yield break;
                }

                yield return ImportTemplate(taskId, definition.objectType, glb);
                if (!cache.TryGetMeshTemplate(taskId, out template))
                {
                    loadingTypes.Remove(definition.objectType);
                    yield break;
                }
            }

            // 3. Swap: one copy per pending placeholder, adopting its transform.
            templatesByObjectType[definition.objectType] = template;
            loadingTypes.Remove(definition.objectType);
            if (pendingByType.TryGetValue(definition.objectType, out var placeholders))
            {
                pendingByType.Remove(definition.objectType);
                foreach (var placeholder in placeholders)
                {
                    if (placeholder == null) continue; // world was regenerated meanwhile
                    PlaceOver(template, placeholder);
                }
            }
        }

        private IEnumerator ImportTemplate(string taskId, string objectType, byte[] glb)
        {
            var import = new GltfImport();
            var loadTask = import.LoadGltfBinary(glb);
            yield return new WaitUntil(() => loadTask.IsCompleted);

            if (loadTask.IsFaulted || !loadTask.Result)
            {
                Debug.LogWarning($"GeneratedMeshLoader: glTF import failed for '{objectType}'; keeping placeholder");
                import.Dispose();
                yield break;
            }

            if (templateRoot == null)
            {
                templateRoot = new GameObject("GeneratedMeshTemplates").transform;
                templateRoot.SetParent(transform, worldPositionStays: false);
            }

            var template = new GameObject($"MeshTemplate_{objectType}");
            template.transform.SetParent(templateRoot, worldPositionStays: false);

            var instantiateTask = import.InstantiateMainSceneAsync(template.transform);
            yield return new WaitUntil(() => instantiateTask.IsCompleted);

            if (instantiateTask.IsFaulted || !instantiateTask.Result)
            {
                Debug.LogWarning($"GeneratedMeshLoader: glTF instantiate failed for '{objectType}'; keeping placeholder");
                Destroy(template);
                import.Dispose();
                yield break;
            }

            // The import owns the meshes/materials the template references;
            // keep it alive until Clear().
            imports.Add(import);
            template.SetActive(false);
            cache.StoreMeshTemplate(taskId, template);
        }

        private static void PlaceOver(GameObject template, GameObject placeholder)
        {
            var placeholderRenderers = placeholder.GetComponentsInChildren<Renderer>();
            float targetHeight = placeholder.transform.localScale.y;
            Vector3 groundPoint = placeholder.transform.position;
            if (placeholderRenderers.Length > 0)
            {
                var placeholderBounds = placeholderRenderers[0].bounds;
                for (int i = 1; i < placeholderRenderers.Length; i++)
                {
                    placeholderBounds.Encapsulate(placeholderRenderers[i].bounds);
                }
                if (placeholderBounds.size.y > 0.0001f) targetHeight = placeholderBounds.size.y;
                groundPoint.y = placeholderBounds.min.y;
            }

            // A mirrored placeholder (negative X scale) mirrors the mesh too.
            bool mirrored = placeholder.transform.localScale.x < 0f;

            // Uniform-scale roots (track pickups) can nest the mesh so it
            // follows bob/spin. Environment placeholders are often non-uniform,
            // so those copies stay siblings and avoid distortion.
            var rootScale = placeholder.transform.localScale;
            bool nest = Mathf.Abs(rootScale.x - rootScale.y) < 0.001f && Mathf.Abs(rootScale.y - rootScale.z) < 0.001f;
            Transform parent = nest ? placeholder.transform : placeholder.transform.parent;

            var copy = Instantiate(template, parent);
            copy.name = $"{placeholder.name}_Mesh";
            copy.SetActive(true);
            copy.transform.position = placeholder.transform.position;
            copy.transform.rotation = placeholder.transform.rotation;
            copy.transform.localScale = Vector3.one;

            var bounds = CombinedBounds(copy);
            if (bounds.HasValue && bounds.Value.size.y > 0.0001f)
            {
                float fit = targetHeight / bounds.Value.size.y;
                copy.transform.localScale = new Vector3(mirrored ? -fit : fit, fit, fit);
                var scaled = CombinedBounds(copy).Value;
                var groundOffset = groundPoint - new Vector3(scaled.center.x, scaled.min.y, scaled.center.z);
                copy.transform.position += groundOffset;
            }

            foreach (var renderer in placeholderRenderers)
            {
                if (renderer != null) renderer.enabled = false;
            }
        }

        private static Bounds? CombinedBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return null;

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }
    }
}
