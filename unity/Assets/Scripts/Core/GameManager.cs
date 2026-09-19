using System;
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
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [SerializeField] private GameConfig config = new GameConfig();
        [SerializeField] private WorldRecipeClient recipeClient;
        [SerializeField] private WorldGenerator worldGenerator;
        [SerializeField] private RaceManager raceManager;
        [SerializeField] private ResultsUI resultsUI;

        public GameConfig Config => config;
        public GameState CurrentState { get; private set; } = GameState.Boot;
        public event Action<GameState> OnStateChanged;

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

        private void Start()
        {
            recipeClient.Configure(config);

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
            recipeClient.RequestWorldRecipe(request, OnRecipeReady, OnRecipeFailed);
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
                BuildWorldAndAdvance(DefaultWorldRecipe.Get());
            }
            else
            {
                TransitionTo(GameState.Input);
            }
        }

        private void BuildWorldAndAdvance(WorldRecipe recipe)
        {
            worldGenerator.Generate(recipe);
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
