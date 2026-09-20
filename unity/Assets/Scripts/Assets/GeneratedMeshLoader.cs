using System.Collections;
using System.Collections.Generic;
using GLTFast;
using MarioKart.AI;
using MarioKart.Core;
using UnityEngine;

namespace MarioKart.AssetsSystem
{
    /// <summary>
    /// Swaps backend-generated meshes in over the primitive placeholders that
    /// EnvironmentGenerator spawned. For every object type that carries a
    /// mesh task: poll GET /assets/{task_id} until ready (or failed / timed
    /// out), download the GLB, import it once with glTFast, then place one
    /// copy at every placeholder of that type and hide the placeholder's
    /// renderer.
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
        private Transform templateRoot;

        // Progress for whoever wants to wait on us (GameManager's optional
        // "hold the Generating screen until meshes land") or show status.
        private readonly Dictionary<string, int> progressByType = new Dictionary<string, int>();
        private int pending;

        /// <summary>True while any mesh is still being polled, downloaded or imported.</summary>
        public bool IsLoading => pending > 0;
        /// <summary>Mesh tasks started by the last Begin().</summary>
        public int Total { get; private set; }
        /// <summary>Tasks finished (swapped in, failed, or timed out).</summary>
        public int Completed => Total - pending;
        /// <summary>0..1 across all tasks, from the backend's per-task progress.</summary>
        public float Progress
        {
            get
            {
                if (Total == 0) return 1f;
                float sum = 0f;
                foreach (var value in progressByType.Values) sum += Mathf.Clamp01(value / 100f);
                return sum / Total;
            }
        }

        /// <summary>Fired whenever progress or completion state changes.</summary>
        public event System.Action OnProgress;

        /// <summary>
        /// Cancel in-flight loads and drop imported templates. Called by
        /// EnvironmentGenerator before it rebuilds the environment.
        /// </summary>
        public void Clear()
        {
            StopAllCoroutines();
            pending = 0;
            Total = 0;
            progressByType.Clear();
            cache.ClearMeshTemplates();
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
        /// spawned for it.
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
                if (!placeholdersByType.TryGetValue(definition.objectType, out var placeholders) || placeholders.Count == 0) continue;

                Total++;
                pending++;
                progressByType[definition.objectType] = 0;
                StartCoroutine(Tracked(definition, placeholders, config));
            }
            OnProgress?.Invoke();
        }

        /// <summary>
        /// Runs one load to completion (however it ends) and then books it
        /// as finished, so IsLoading/Completed stay honest on every exit path.
        /// </summary>
        private IEnumerator Tracked(AssetDefinition definition, List<GameObject> placeholders, GameConfig config)
        {
            yield return LoadAndSwap(definition, placeholders, config);
            progressByType[definition.objectType] = 100;
            pending = Mathf.Max(0, pending - 1);
            OnProgress?.Invoke();
        }

        private void ReportProgress(string objectType, int percent)
        {
            progressByType[objectType] = percent;
            OnProgress?.Invoke();
        }

        private IEnumerator LoadAndSwap(AssetDefinition definition, List<GameObject> placeholders, GameConfig config)
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

                if (status != null) ReportProgress(definition.objectType, status.progress);
                if (status != null && status.status == MeshTaskStatus.Ready) break;

                if (status != null && status.status == MeshTaskStatus.Failed)
                {
                    Debug.LogWarning($"GeneratedMeshLoader: mesh for '{definition.objectType}' failed ({status.error}); keeping placeholder");
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
                    yield break;
                }

                yield return ImportTemplate(taskId, definition.objectType, glb);
                if (!cache.TryGetMeshTemplate(taskId, out template))
                {
                    yield break;
                }
            }

            // 3. Swap: one copy per placeholder, adopting its transform.
            foreach (var placeholder in placeholders)
            {
                if (placeholder == null) continue; // world was regenerated meanwhile
                PlaceOver(template, placeholder);
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
            var placeholderRenderer = placeholder.GetComponent<Renderer>();
            // Match the placeholder's visual height, and rest the mesh's
            // bottom where the placeholder's bottom is (EnvironmentGenerator
            // stands placeholders on the ground, with per-instance scale,
            // tilt and sink -- all of which should carry over to the mesh).
            float targetHeight = placeholderRenderer != null ? placeholderRenderer.bounds.size.y : placeholder.transform.localScale.y;
            Vector3 groundPoint = placeholder.transform.position;
            if (placeholderRenderer != null) groundPoint.y = placeholderRenderer.bounds.min.y;

            // A mirrored placeholder (negative X scale) mirrors the mesh too.
            bool mirrored = placeholder.transform.localScale.x < 0f;

            // Copies go next to the placeholder, not under it, so the
            // placeholder's non-uniform scale doesn't distort the mesh.
            var copy = Instantiate(template, placeholder.transform.parent);
            copy.name = $"{placeholder.name}_Mesh";
            copy.SetActive(true);
            copy.transform.position = placeholder.transform.position;
            copy.transform.rotation = placeholder.transform.rotation;
            copy.transform.localScale = Vector3.one;

            var bounds = CombinedBounds(copy);
            if (bounds.HasValue && bounds.Value.size.y > 0.0001f)
            {
                float scale = targetHeight / bounds.Value.size.y;
                copy.transform.localScale = new Vector3(mirrored ? -scale : scale, scale, scale);
                var scaled = CombinedBounds(copy).Value;
                var groundOffset = groundPoint - new Vector3(scaled.center.x, scaled.min.y, scaled.center.z);
                copy.transform.position += groundOffset;
            }

            if (placeholderRenderer != null)
            {
                placeholderRenderer.enabled = false;
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
