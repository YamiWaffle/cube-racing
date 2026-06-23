using CubeRacing.Domain.Entities;
using CubeRacing.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CubeRacing.Infrastructure.Persistence.Repositories;

public class BetRepository : IBetRepository
{
    private readonly AppDbContext _db;
    public BetRepository(AppDbContext db) => _db = db;

    public Task<bool> ExistsAsync(Guid playerId, Guid sessionId, CancellationToken ct = default)
        => _db.Bets.AnyAsync(b => b.PlayerId == playerId && b.SessionId == sessionId, ct);

    public async Task AddAsync(Bet bet, CancellationToken ct = default)
    {
        _db.Bets.Add(bet);
        await _db.SaveChangesAsync(ct);
    }

    public Task<List<Bet>> GetBySessionAsync(Guid sessionId, CancellationToken ct = default)
        => _db.Bets.Where(b => b.SessionId == sessionId).ToListAsync(ct);

    public async Task UpdateAsync(Bet bet, CancellationToken ct = default)
    {
        _db.Bets.Update(bet);
        await _db.SaveChangesAsync(ct);
    }
}
