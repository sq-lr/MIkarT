using System;
using System.Collections;
using System.Collections.Generic;
using MarioKart.AI;
using MarioKart.Racing;
using MarioKart.UI;
using MarioKart.World;
using UnityEngine;

namespace MarioKart.Core
{
    /// <summary>
    /// Owns the single source of truth for game flow (see GameState). UI
    /// panels read CurrentState / OnStateChanged and call back into
    /// GameManager -- they never mutate state themselves.
    ///
    /// Runs before every other script so Instance is set before any UI
    /// panel's OnEnable reads it (Awake order across scene objects is
    /// otherwise unspecified).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [SerializeField] private GameConfig config = new GameConfig();
        [SerializeField] private WorldRecipeClient recipeClient;
        [SerializeField] private MeshAssetClient meshAssetClient;
        [SerializeField] private WorldGenerator worldGenerator;
        [SerializeField] private RaceManager raceManager;
        [SerializeField] private ResultsUI resultsUI;

        public GameConfig Config => config;
        public GameState CurrentState { get; private set; } = GameState.Boot;
        public event Action<GameState> OnStateChanged;

        /// <summary>
        /// Human-readable progress while in GameState.Generating ("Waiting
        /// for 3D models... 1/2 ready, 45%"). UI shows it; nothing else
        /// depends on it.
        /// </summary>
        public string GenerationStatus { get; private set; } = "";
        public event Action<string> OnGenerationStatusChanged;

        private Coroutine meshWait;

        // Only these transitions are allowed; an illegal request is logged
        // and ignored rather than crashing a misconfigured UI panel.
        private static readonly Dictionary<GameState, GameState[]> AllowedTransitions = new()
        {
            { GameState.Boot, new[] { GameState.Input } },
            { GameState.Input, new[] { GameState.Generating } },
            { GameState.Generating, new[] { GameState.WorldReady, GameState.Input } },
            { GameState.WorldReady, new[] { GameState.Countdown } },
            { GameState.Countdown, new[] { GameState.Racing } },
            { GameState.Racing, new[] { GameState.Finished } },
            { GameState.Finished, new[] { GameState.Results } },
            { GameState.Results, new[] { GameState.Boot } },
        };

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        // A script recompile during Play mode wipes statics and re-runs
        // OnEnable (not Awake) on every object. Restore Instance here so the
        // UI panels' OnEnable -- which runs after ours, see the execution
        // order above -- never sees null after a hot reload.
        private void OnEnable()
        {
            if (Instance == null) Instance = this;
        }

        private void Start()
        {
            recipeClient.Configure(config);
            if (meshAssetClient != null)
            {
                meshAssetClient.Configure(config);
            }

            if (raceManager != null)
            {
                raceManager.OnRaceFinished += OnRaceFinished;
            }
        }

        public void TransitionTo(GameState next)
        {
            if (!AllowedTransitions.TryGetValue(CurrentState, out var allowed) || Array.IndexOf(allowed, next) < 0)
            {
                Debug.LogError($"Illegal state transition: {CurrentState} -> {next}");
                return;
            }

            CurrentState = next;
            OnStateChanged?.Invoke(CurrentState);
        }

        /// <summary>
        /// Entry point for the single world-generation input (one image +
        /// one description, shared by both players).
        /// </summary>
        public void SubmitWorldInput(WorldGenerationRequest request)
        {
            TransitionTo(GameState.Generating);
            SetGenerationStatus("Generating your world...");
            recipeClient.RequestWorldRecipe(request, OnRecipeReady, OnRecipeFailed);
        }

        private void SetGenerationStatus(string status)
        {
            if (status == GenerationStatus) return;
            GenerationStatus = status;
            OnGenerationStatusChanged?.Invoke(status);
        }

        private void OnRecipeReady(WorldRecipe recipe)
        {
            BuildWorldAndAdvance(recipe);
        }

        private void OnRecipeFailed(string error)
        {
            Debug.LogWarning($"World generation failed, falling back to default world: {error}");
            if (config.fallbackToDefaultOnError)
            {
                StartCoroutine(FallBackToDefaultWorld(error));
            }
            else
            {
                TransitionTo(GameState.Input);
            }
        }

        // Show *why* we're using the offline world for a few seconds before
        // building it. A silent fallback is indistinguishable from success
        // and sends people debugging the wrong thing.
        private IEnumerator FallBackToDefaultWorld(string error)
        {
            SetGenerationStatus($"Couldn't generate from the backend:\n{error}\n\nUsing the offline world instead.");
            yield return new WaitForSecondsRealtime(3f);
            BuildWorldAndAdvance(DefaultWorldRecipe.Get());
        }

        private void BuildWorldAndAdvance(WorldRecipe recipe)
        {
            SetGenerationStatus("Building the track...");
            worldGenerator.Generate(recipe);

            // Optionally hold here until the generated meshes have swapped in.
            // The world (with placeholders) already exists behind the
            // Generating screen; only the countdown is deferred.
            var loader = worldGenerator.MeshLoader;
            if (config.waitForGeneratedMeshes && loader != null && loader.IsLoading)
            {
                if (meshWait != null) StopCoroutine(meshWait);
                meshWait = StartCoroutine(WaitForMeshesThenAdvance(loader));
                return;
            }

            TransitionTo(GameState.WorldReady);
        }

        private IEnumerator WaitForMeshesThenAdvance(MarioKart.AssetsSystem.GeneratedMeshLoader loader)
        {
            float deadline = Time.realtimeSinceStartup + config.meshWaitTimeoutSeconds;
            while (loader.IsLoading && Time.realtimeSinceStartup < deadline)
            {
                int percent = Mathf.RoundToInt(loader.Progress * 100f);
                SetGenerationStatus($"Waiting for 3D models...\n{loader.Completed}/{loader.Total} ready · {percent}%");
                yield return new WaitForSecondsRealtime(0.25f);
            }

            if (loader.IsLoading)
            {
                Debug.LogWarning($"GameManager: generated meshes still pending after {config.meshWaitTimeoutSeconds}s; starting on placeholders");
            }

            meshWait = null;
            TransitionTo(GameState.WorldReady);
        }

        public void BeginCountdown()
        {
            TransitionTo(GameState.Countdown);
        }

        public void BeginRace()
        {
            TransitionTo(GameState.Racing);
        }

        private void OnRaceFinished(RaceResult result)
        {
            TransitionTo(GameState.Finished);
            TransitionTo(GameState.Results);
            resultsUI?.DisplayResult(result);
        }
    }
}
