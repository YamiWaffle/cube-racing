using CubeRacing.Application.Dtos;
using CubeRacing.Domain.Interfaces;

namespace CubeRacing.Application.UseCases;

public class GetLeaderboard
{
    private readonly IPlayerRepository _players;
    public GetLeaderboard(IPlayerRepository players) => _players = players;

    public async Task<List<LeaderboardEntryDto>> ExecuteAsync(CancellationToken ct = default)
    {
        var top = await _players.GetTopByWinningsAsync(20, ct);
        return top.Select(p => new LeaderboardEntryDto(p.Nickname, p.CorrectBets, p.TotalChipsWon)).ToList();
    }
}
