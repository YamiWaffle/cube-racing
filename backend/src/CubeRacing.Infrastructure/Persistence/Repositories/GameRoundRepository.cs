using CubeRacing.Domain.Entities;
using CubeRacing.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CubeRacing.Infrastructure.Persistence.Repositories;

public class GameRoundRepository : IGameRoundRepository
{
    private readonly AppDbContext _db;
    public GameRoundRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(GameRound round, CancellationToken ct = default)
    {
        _db.GameRounds.Add(round);
        await _db.SaveChangesAsync(ct);
    }

    public Task<GameRound?> GetLatestBySessionAsync(Guid sessionId, CancellationToken ct = default)
        => _db.GameRounds
              .Where(r => r.SessionId == sessionId)
              .OrderByDescending(r => r.RoundNumber)
              .FirstOrDefaultAsync(ct);
}
