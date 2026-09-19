using System;
using System.Collections;
using MarioKart.Core;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace MarioKart.AI
{
    /// <summary>
    /// Unity's only network call: POST the single world input to the
    /// backend's /generate-world endpoint and parse the WorldRecipe out of
    /// its response. The AI never touches Unity objects directly -- this is
    /// the entire surface of the contract.
    /// </summary>
    public class WorldRecipeClient : MonoBehaviour
    {
        private GameConfig config = new GameConfig();

        /// <summary>
        /// GameManager calls this once at startup so both components share
        /// the same GameConfig instance instead of each owning their own.
        /// </summary>
        public void Configure(GameConfig gameConfig)
        {
            config = gameConfig;
        }

        public void RequestWorldRecipe(WorldGenerationRequest request, Action<WorldRecipe> onSuccess, Action<string> onError)
        {
            StartCoroutine(PostWorldRequest(request, onSuccess, onError));
        }

        private IEnumerator PostWorldRequest(WorldGenerationRequest request, Action<WorldRecipe> onSuccess, Action<string> onError)
        {
            var form = new WWWForm();
            form.AddBinaryData("image", request.imageBytes, request.imageFileName, "image/jpeg");
            form.AddField("description", request.description);

            string url = $"{config.backendBaseUrl}/generate-world";
            using UnityWebRequest www = UnityWebRequest.Post(url, form);
            www.timeout = Mathf.CeilToInt(config.requestTimeoutSeconds);

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(www.error);
                yield break;
            }

            try
            {
                var response = JsonConvert.DeserializeObject<WorldRecipeResponse>(www.downloadHandler.text);
                if (response?.world_recipe == null)
                {
                    onError?.Invoke("response missing world_recipe");
                    yield break;
                }
                if (response.world_recipe.version != 1)
                {
                    Debug.LogWarning($"Unexpected WorldRecipe version: {response.world_recipe.version}");
                }
                onSuccess?.Invoke(response.world_recipe);
            }
            catch (Exception e)
            {
                onError?.Invoke($"failed to parse world_recipe: {e.Message}");
            }
        }
    }
}
