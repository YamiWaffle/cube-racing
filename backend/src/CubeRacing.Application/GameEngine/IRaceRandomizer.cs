namespace CubeRacing.Application.GameEngine;

public interface IRaceRandomizer
{
    int RollDice();
    IReadOnlyList<int> ShuffleOrder(IReadOnlyList<int> npcIds);
}

public class DefaultRaceRandomizer : IRaceRandomizer
{
    private readonly Random _rng;
    public DefaultRaceRandomizer(Random? rng = null) => _rng = rng ?? Random.Shared;
    public int RollDice() => _rng.Next(1, 4);
    public IReadOnlyList<int> ShuffleOrder(IReadOnlyList<int> npcIds)
        => npcIds.OrderBy(_ => _rng.Next()).ToList();
}
