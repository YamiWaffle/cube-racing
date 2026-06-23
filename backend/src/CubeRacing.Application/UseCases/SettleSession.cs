using CubeRacing.Application.Dtos;
using CubeRacing.Application.GameEngine;
using CubeRacing.Domain.Interfaces;

namespace CubeRacing.Application.UseCases;

public class SettleSession
{
    private readonly IGameSessionRepository _sessions;
    private readonly IBetRepository _bets;
    private readonly IPlayerRepository _players;
    private readonly SettlementCalculator _calculator;

    public SettleSession(IGameSessionRepository sessions, IBetRepository bets,
        IPlayerRepository players, SettlementCalculator calculator)
    {
        _sessions = sessions;
        _bets = bets;
        _players = players;
        _calculator = calculator;
    }

    public async Task<SettlementResultDto> ExecuteAsync(Guid sessionId, int winnerNpcId, CancellationToken ct = default)
    {
        var session = await _sessions.GetByIdAsync(sessionId, ct)
            ?? throw new InvalidOperationException($"Session {sessionId} not found.");

        var bets = await _bets.GetBySessionAsync(sessionId, ct);
        var results = _calculator.Calculate(winnerNpcId, bets);

        var playerResults = new List<PlayerResultDto>();
        foreach (var result in results)
        {
            var bet = bets.First(b => b.Id == result.BetId);
            bet.SetWinAmount(result.WinAmount);
            await _bets.UpdateAsync(bet, ct);

            if (result.WinAmount > 0)
            {
                var player = await _players.GetByIdAsync(bet.PlayerId, ct);
                if (player is not null)
                {
                    player.AddWinnings(result.WinAmount);
                    await _players.UpdateAsync(player, ct);
                }
            }
            playerResults.Add(new PlayerResultDto(bet.PlayerId, result.WinAmount));
        }

        session.Complete(winnerNpcId);
        await _sessions.UpdateAsync(session, ct);

        var top = await _players.GetTopByWinningsAsync(5, ct);
        var leaderboard = top.Select(p => new LeaderboardEntryDto(p.Nickname, p.CorrectBets, p.TotalChipsWon)).ToList();
        return new SettlementResultDto(winnerNpcId, playerResults, leaderboard);
    }
}
