namespace CubeRacing.Application.GameEngine;

public record RoundAction(int NpcId, int DiceRoll, int FromSquare, int ToSquare, List<int> CarriedNpcIds);
public record RoundResult(List<RoundAction> Actions, Dictionary<string, List<int>> SquareStacks, int? Winner);

public class RaceSimulator
{
    private readonly List<int>[] _squares;
    private readonly int _mapLength;
    private readonly IRaceRandomizer _randomizer;
    private readonly int[] _npcIds;

    public RaceSimulator(int npcCount, int mapLength, IRaceRandomizer? randomizer = null)
    {
        _mapLength = mapLength;
        _randomizer = randomizer ?? new DefaultRaceRandomizer();
        _npcIds = new int[npcCount];
        _squares = InitSquares(mapLength + 1);
        for (int i = 0; i < npcCount; i++)
        {
            var npcId = i + 1;
            _npcIds[i] = npcId;
            _squares[0].Add(npcId);
        }
    }

    private RaceSimulator(int[] npcIds, List<int>[] squares, int mapLength, IRaceRandomizer randomizer)
    {
        _npcIds = npcIds;
        _squares = squares;
        _mapLength = mapLength;
        _randomizer = randomizer;
    }

    public static RaceSimulator CreateWithPositions(
        Dictionary<int, List<int>> positions, int mapLength, IRaceRandomizer randomizer)
    {
        var npcIds = positions.Values.SelectMany(x => x).ToArray();
        var squares = InitSquares(mapLength + 1);
        foreach (var (sq, stack) in positions)
            squares[sq].AddRange(stack);
        
        return new RaceSimulator(npcIds, squares, mapLength, randomizer);
    }

    private static List<int>[] InitSquares(int size)
    {
        var arr = new List<int>[size];
        for (int i = 0; i < size; i++) arr[i] = new List<int>();
        return arr;
    }

    public RoundResult SimulateRound()
    {
        var order = _randomizer.ShuffleOrder(_npcIds);
        var actions = new List<RoundAction>();

        foreach (var npcId in order)
        {
            var (fromSquare, stackIdx) = FindNpc(npcId);
            
            // already at finish, skip
            if (fromSquare == _mapLength) continue;
            
            var dice = _randomizer.RollDice();
            var moving = _squares[fromSquare].Skip(stackIdx).ToList();
            _squares[fromSquare] = _squares[fromSquare].Take(stackIdx).ToList();
            
            var toSquare = Math.Min(fromSquare + dice, _mapLength);
            _squares[toSquare].AddRange(moving);

            var roundAction = new RoundAction(
                npcId,
                dice, 
                fromSquare,
                toSquare, 
                moving.Skip(1).ToList());
            actions.Add(roundAction);
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
