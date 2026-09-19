namespace MarioKart.Core
{
    /// <summary>
    /// High-level game flow. GameManager owns transitions between these;
    /// UI panels react to state, they never drive it.
    /// </summary>
    public enum GameState
    {
        Boot,
        Input,
        Generating,
        WorldReady,
        Countdown,
        Racing,
        Finished,
        Results
    }
}
