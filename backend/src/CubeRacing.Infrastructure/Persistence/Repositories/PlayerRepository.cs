using CubeRacing.Domain.Entities;
using CubeRacing.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CubeRacing.Infrastructure.Persistence.Repositories;

public class PlayerRepository : IPlayerRepository
{
    private readonly AppDbContext _db;
    public PlayerRepository(AppDbContext db) => _db = db;

    public Task<Player?> GetByTokenAsync(Guid token, CancellationToken ct = default)
        => _db.Players.FirstOrDefaultAsync(p => p.Token == token, ct);

    public Task<Player?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Players.FindAsync([id], ct).AsTask();

    public async Task AddAsync(Player player, CancellationToken ct = default)
    {
        _db.Players.Add(player);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Player player, CancellationToken ct = default)
    {
        _db.Players.Update(player);
        await _db.SaveChangesAsync(ct);
    }

    public Task<List<Player>> GetTopByWinningsAsync(int count, CancellationToken ct = default)
        => _db.Players.OrderByDescending(p => p.TotalChipsWon).Take(count).ToListAsync(ct);
}
