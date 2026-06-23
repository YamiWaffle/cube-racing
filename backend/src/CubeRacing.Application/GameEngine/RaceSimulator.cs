namespace CubeRacing.Application.GameEngine;

public record RoundAction(int NpcId, int DiceRoll, int FromSquare, int ToSquare, List<int> CarriedNpcIds);
public record RoundResult(List<RoundAction> Actions, Dictionary<string, List<int>> SquareStacks, int? Winner);

public class RaceSimulator
{
    private readonly List<int>[] _squares;
    private readonly int _mapLength;
    private readonly IRaceRandomizer _randomizer;

    public RaceSimulator(int npcCount, int mapLength, IRaceRandomizer? randomizer = null)
    {
        _mapLength = mapLength;
        _randomizer = randomizer ?? new DefaultRaceRandomizer();
        _squares = InitSquares(mapLength + 1);
        for (int id = 1; id <= npcCount; id++)
            _squares[0].Add(id);
    }

    private RaceSimulator(List<int>[] squares, int mapLength, IRaceRandomizer randomizer)
    {
        _squares = squares;
        _mapLength = mapLength;
        _randomizer = randomizer;
    }

    public static RaceSimulator CreateWithPositions(
        Dictionary<int, List<int>> positions, int mapLength, IRaceRandomizer randomizer)
    {
        var squares = InitSquares(mapLength + 1);
        foreach (var (sq, stack) in positions)
            squares[sq].AddRange(stack);
        return new RaceSimulator(squares, mapLength, randomizer);
    }

    private static List<int>[] InitSquares(int size)
    {
        var arr = new List<int>[size];
        for (int i = 0; i < size; i++) arr[i] = new List<int>();
        return arr;
    }

    public RoundResult SimulateRound()
    {
        // Capture initial stack groups: each NPC maps to the set of NPCs that started in the same stack
        // The "group leader" is the first NPC in the shuffled order that belongs to the group
        var npcToGroup = new Dictionary<int, int>(); // npcId -> groupId (groupId = square index at start)
        var allNpcs = new List<int>();
        for (int s = 0; s <= _mapLength; s++)
        {
            foreach (int npcId in _squares[s])
            {
                npcToGroup[npcId] = s;
                allNpcs.Add(npcId);
            }
        }

        var order = _randomizer.ShuffleOrder(allNpcs);
        var actions = new List<RoundAction>();
        var movedGroups = new HashSet<int>(); // groups that have already had their leader move

        foreach (int npcId in order)
        {
            // Skip NPCs not in our game (edge case safety)
            if (!npcToGroup.TryGetValue(npcId, out int groupId)) continue;

            // Skip if this group has already had its leader take a turn this round
            if (movedGroups.Contains(groupId)) continue;

            var (fromSq, idx) = FindNpc(npcId);
            if (fromSq == _mapLength) continue; // already at finish, skip

            // Mark this group as having moved
            movedGroups.Add(groupId);

            int dice = _randomizer.RollDice();
            var moving = _squares[fromSq].Skip(idx).ToList();
            _squares[fromSq] = _squares[fromSq].Take(idx).ToList();

            int toSq = Math.Min(fromSq + dice, _mapLength);
            _squares[toSq].AddRange(moving);

            actions.Add(new RoundAction(npcId, dice, fromSq, toSq, moving.Skip(1).ToList()));
        }

        return new RoundResult(actions, GetSquareStacks(), GetWinner());
    }

    public int? GetWinner()
    {
        var finish = _squares[_mapLength];
        return finish.Count > 0 ? finish[^1] : null;
    }

    public Dictionary<string, List<int>> GetSquareStacks()
        => _squares
            .Select((stack, idx) => (idx, stack))
            .Where(x => x.stack.Count > 0)
            .ToDictionary(x => x.idx.ToString(), x => x.stack.ToList());

    private (int square, int index) FindNpc(int npcId)
    {
        for (int s = 0; s <= _mapLength; s++)
        {
            int i = _squares[s].IndexOf(npcId);
            if (i >= 0) return (s, i);
        }
        throw new InvalidOperationException($"NPC {npcId} not found in any square.");
    }
}
