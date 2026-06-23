using CubeRacing.Application.GameEngine;

namespace CubeRacing.Tests.Helpers;

public class FixedRaceRandomizer : IRaceRandomizer
{
    private readonly List<int> _fixedOrder;
    private readonly Queue<int> _diceQueue;

    public FixedRaceRandomizer(IEnumerable<int> order, IEnumerable<int> dice)
    {
        _fixedOrder = order.ToList();
        _diceQueue = new Queue<int>(dice);
    }

    public int RollDice() => _diceQueue.Dequeue();
    public IReadOnlyList<int> ShuffleOrder(IReadOnlyList<int> _) => _fixedOrder;
}
