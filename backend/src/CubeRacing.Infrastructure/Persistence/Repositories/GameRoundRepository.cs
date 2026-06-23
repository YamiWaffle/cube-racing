using CubeRacing.Domain.Entities;
using CubeRacing.Domain.Interfaces;

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
}
