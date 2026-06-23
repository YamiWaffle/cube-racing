namespace CubeRacing.Domain.Events;

public record RoundExecutedEvent(
    Guid SessionId,
    int RoundNumber,
    List<RoundActionDto> Actions,
    Dictionary<string, List<int>> SquareStacks,
    int? Winner);

public record RoundActionDto(int NpcId, int DiceRoll, int FromSquare, int ToSquare, List<int> CarriedNpcIds);
