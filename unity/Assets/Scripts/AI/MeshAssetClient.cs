using System;
using System.Collections;
using MarioKart.Core;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace MarioKart.AI
{
    /// <summary>
    /// Status of one asynchronously generated mesh, as returned by
    /// GET /assets/{task_id}. Mirrors the backend's MeshTaskStatus.
    /// </summary>
    [Serializable]
    public class MeshTaskStatus
    {
        public const string Pending = "pending";
        public const string Ready = "ready";
        public const string Failed = "failed";

        public string status;
        public int progress;
        public string error;
    }

    /// <summary>
    /// Unity's second (and last) backend surface, alongside WorldRecipeClient:
    /// polls the status of a mesh the backend kicked off during
    /// /generate-world, then downloads the finished GLB. Unity never talks
    /// to the mesh provider (Meshy) directly -- the backend proxies it.
    /// </summary>
    public class MeshAssetClient : MonoBehaviour
    {
        private GameConfig config = new GameConfig();

        public void Configure(GameConfig gameConfig)
        {
            config = gameConfig;
        }

        public void PollStatus(string taskId, Action<MeshTaskStatus> onResult, Action<string> onError)
        {
            StartCoroutine(GetStatus(taskId, onResult, onError));
        }

        public void DownloadModel(string taskId, Action<byte[]> onResult, Action<string> onError)
        {
            StartCoroutine(GetModel(taskId, onResult, onError));
        }

        private IEnumerator GetStatus(string taskId, Action<MeshTaskStatus> onResult, Action<string> onError)
        {
            string url = $"{config.backendBaseUrl}/assets/{taskId}";
            using UnityWebRequest www = UnityWebRequest.Get(url);
            www.timeout = Mathf.CeilToInt(config.requestTimeoutSeconds);

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(www.error);
                yield break;
            }

            try
            {
                var status = JsonConvert.DeserializeObject<MeshTaskStatus>(www.downloadHandler.text);
                if (status == null || string.IsNullOrEmpty(status.status))
                {
                    onError?.Invoke("response missing status");
                    yield break;
                }
                onResult?.Invoke(status);
            }
            catch (Exception e)
            {
                onError?.Invoke($"failed to parse asset status: {e.Message}");
            }
        }

        private IEnumerator GetModel(string taskId, Action<byte[]> onResult, Action<string> onError)
        {
            string url = $"{config.backendBaseUrl}/assets/{taskId}/model.glb";
            using UnityWebRequest www = UnityWebRequest.Get(url);
            // GLBs can be several MB; give the download more room than a JSON call.
            www.timeout = Mathf.CeilToInt(config.requestTimeoutSeconds * 4f);

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(www.error);
                yield break;
            }

            onResult?.Invoke(www.downloadHandler.data);
        }
    }
}
