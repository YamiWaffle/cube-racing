using CubeRacing.Application.Config;
using CubeRacing.Application.Interfaces;
using CubeRacing.Domain.Entities;
using CubeRacing.Domain.Enums;
using CubeRacing.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CubeRacing.Application.UseCases;

public record PlaceBetRequest(Guid PlayerId, Guid SessionId, int NpcId, int Amount);

public enum PlaceBetError { SessionNotFound, BettingClosed, AlreadyBet, InsufficientChips, InvalidNpcId, InvalidAmount }
public record PlaceBetResult(bool Success, PlaceBetError? Error = null);

public class PlaceBet
{
    private readonly IGameSessionRepository _sessions;
    private readonly IBetRepository _bets;
    private readonly IPlayerRepository _players;
    private readonly IGameHubNotifier _hub;
    private readonly DbContext _db;
    private readonly GameSettings _settings;

    public PlaceBet(IGameSessionRepository sessions, IBetRepository bets,
        IPlayerRepository players, IGameHubNotifier hub,
        DbContext db, IOptions<GameSettings> settings)
    {
        _sessions = sessions;
        _bets = bets;
        _players = players;
        _hub = hub;
        _db = db;
        _settings = settings.Value;
    }

    public async Task<PlaceBetResult> ExecuteAsync(PlaceBetRequest req, CancellationToken ct = default)
    {
        if (req.Amount <= 0)
            return new PlaceBetResult(false, PlaceBetError.InvalidAmount);

        if (req.NpcId < 1 || req.NpcId > _settings.NpcCount)
            return new PlaceBetResult(false, PlaceBetError.InvalidNpcId);

        var session = await _sessions.GetByIdAsync(req.SessionId, ct);
        if (session is null)
            return new PlaceBetResult(false, PlaceBetError.SessionNotFound);

        if (session.Status != GameStatus.Betting || DateTime.UtcNow > session.BettingDeadline)
            return new PlaceBetResult(false, PlaceBetError.BettingClosed);

        if (await _bets.ExistsAsync(req.PlayerId, req.SessionId, ct))
            return new PlaceBetResult(false, PlaceBetError.AlreadyBet);

        var player = await _players.GetByIdAsync(req.PlayerId, ct);
        if (player is null || player.ChipsBalance < req.Amount)
            return new PlaceBetResult(false, PlaceBetError.InsufficientChips);

        using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            player.DeductChips(req.Amount);
            await _players.UpdateAsync(player, ct);

            var bet = Bet.Create(req.PlayerId, req.SessionId, req.NpcId, req.Amount);
            await _bets.AddAsync(bet, ct);

            session.AddToPool(req.Amount);
            await _sessions.UpdateAsync(session, ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        // Broadcast updated odds after successful commit
        var allBets = await _bets.GetBySessionAsync(req.SessionId, ct);
        var odds = CalculateOdds(allBets, session.TotalPool);
        await _hub.NotifyOddsUpdatedAsync(req.SessionId, odds);

        return new PlaceBetResult(true);
    }

    private object CalculateOdds(List<Bet> bets, int totalPool)
    {
        var poolByNpc = bets.GroupBy(b => b.NpcId).ToDictionary(g => g.Key, g => g.Sum(b => b.Amount));
        return Enumerable.Range(1, _settings.NpcCount).Select(id =>
        {
            double odds = poolByNpc.TryGetValue(id, out var pool) && pool > 0
                ? (double)totalPool / pool
                : _settings.DefaultOdds;
            return new { npcId = id, odds };
        }).ToList();
    }
}
