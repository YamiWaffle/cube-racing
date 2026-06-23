namespace CubeRacing.Application.Dtos;

public record SettlementResultDto(int WinnerNpcId, List<PlayerResultDto> PlayerResults, List<LeaderboardEntryDto> TopLeaderboard);
public record PlayerResultDto(Guid PlayerId, int WinAmount);
