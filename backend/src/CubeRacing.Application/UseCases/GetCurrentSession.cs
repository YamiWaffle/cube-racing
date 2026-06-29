using CubeRacing.Application.Config;
using CubeRacing.Application.Dtos;
using CubeRacing.Application.Interfaces;
using CubeRacing.Domain.Enums;
using CubeRacing.Domain.Interfaces;
using Microsoft.Extensions.Options;

namespace CubeRacing.Application.UseCases;

public class GetCurrentSession
{
    private readonly ICurrentSessionStore _store;
    private readonly IGameSessionRepository _sessions;
    private readonly IBetRepository _bets;
    private readonly GameSettings _settings;

    public GetCurrentSession(ICurrentSessionStore store, IGameSessionRepository sessions, IBetRepository bets,
        IOptions<GameSettings> settings)
    {
        _store = store;
        _sessions = sessions;
        _bets = bets;
        _settings = settings.Value;
    }

    public async Task<CurrentSessionDto?> ExecuteAsync(CancellationToken ct = default)
    {
        if (_store.CurrentSessionId is null) return null;

        var session = await _sessions.GetByIdAsync(_store.CurrentSessionId.Value, ct);
        if (session is null) return null;

        var allBets = await _bets.GetBySessionAsync(session.Id, ct);
        var poolByNpc = allBets.GroupBy(b => b.NpcId).ToDictionary(g => g.Key, g => g.Sum(b => b.Amount));
        var npcOdds = Enumerable.Range(1, _settings.NpcCount).Select(id =>
        {
            double odds = poolByNpc.TryGetValue(id, out var pool) && pool > 0 && session.TotalPool > 0
                ? (double)session.TotalPool / pool
                : _settings.DefaultOdds;
            return new NpcOddsDto(id, odds);
        }).ToList();

        int? remaining = session.Status == GameStatus.Betting
            ? Math.Max(0, (int)(session.BettingDeadline - DateTime.UtcNow).TotalSeconds)
            : null;

        return new CurrentSessionDto(
            session.Id, session.Status.ToString(), remaining, npcOdds,
            session.MapLength, _store.RaceStartsAt, _store.BettingStartsAt);
    }
}
