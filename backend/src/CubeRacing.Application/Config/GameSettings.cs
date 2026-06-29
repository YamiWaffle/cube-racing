namespace CubeRacing.Application.Config;

public class GameSettings
{
    public int    MapLength              { get; init; } = 20;
    public int    NpcCount              { get; init; } = 4;
    public int    BettingDurationSeconds { get; init; } = 60;
    public int    WaitingDurationSeconds { get; init; } = 30;
    public int    RaceStartDelaySeconds  { get; init; } = 30;
    public int    RoundIntervalMs        { get; init; } = 3000;
    public int    InitialChips           { get; init; } = 1000;
    public double DefaultOdds            { get; init; } = 1.3;
}
