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
        _squares = InitSquares(mapLength);
        for (int i = 0; i < npcCount; i++)
        {
            var npcId = i + 1;
            _npcIds[i] = npcId;
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
        var squares = InitSquares(mapLength);
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
        bool raceEnded = false;

        foreach (var npcId in order)
        {
            var (fromSquare, stackIdx) = FindNpc(npcId);

            var notStartYet = fromSquare == -1;

            // already at finish, skip entirely
            if (fromSquare == _mapLength - 1) continue;

            var dice = _randomizer.RollDice();

            if (raceEnded)
            {
                // Race is over — include dice roll for UI display but don't move the NPC
                actions.Add(new RoundAction(npcId, dice, fromSquare, fromSquare, new List<int>()));
                continue;
            }

            List<int> moving;
            if (notStartYet)
            {
                moving = new List<int> { npcId };
            }
            else
            {
                moving = _squares[fromSquare].Skip(stackIdx).ToList();
                _squares[fromSquare] = _squares[fromSquare].Take(stackIdx).ToList();
            }

            var toSquare = Math.Min(fromSquare + dice, _mapLength - 1);
            _squares[toSquare].AddRange(moving);

            var roundAction = new RoundAction(
                npcId,
                dice,
                fromSquare,
                toSquare,
                moving.Skip(1).ToList());
            actions.Add(roundAction);

            if (toSquare == _mapLength - 1)
                raceEnded = true;
        }

        return new RoundResult(actions, GetSquareStacks(), GetWinner());
    }

    public int? GetWinner()
    {
        var finish = _squares[_mapLength - 1];
        return finish.Count > 0 ? finish[^1] : null;
    }

    public Dictionary<string, List<int>> GetSquareStacks()
        => _squares
            .Select((stack, idx) => (idx, stack))
            .Where(x => x.stack.Count > 0)
            .ToDictionary(x => x.idx.ToString(), x => x.stack.ToList());

    private (int square, int index) FindNpc(int npcId)
    {
        for (int s = 0; s < _mapLength; s++)
        {
            int i = _squares[s].IndexOf(npcId);
            if (i >= 0) return (s, i);
        }
        
        return (-1, -1);
    }
}
