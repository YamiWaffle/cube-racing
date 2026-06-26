namespace CubeRacing.Application.Dtos;

public record CurrentSessionDto(
    Guid              SessionId,
    string            Status,
    int?              BettingSecondsRemaining,
    List<NpcOddsDto>  NpcOdds,
    int               MapLength,
    DateTime?         RaceStartsAt = null);
