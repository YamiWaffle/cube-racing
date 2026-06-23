using CubeRacing.Domain.Entities;
using CubeRacing.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CubeRacing.Infrastructure.Persistence.Repositories;

public class GameSessionRepository : IGameSessionRepository
{
    private readonly AppDbContext _db;
    public GameSessionRepository(AppDbContext db) => _db = db;

    public Task<GameSession?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.GameSessions.FindAsync([id], ct).AsTask();

    public async Task AddAsync(GameSession session, CancellationToken ct = default)
    {
        _db.GameSessions.Add(session);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(GameSession session, CancellationToken ct = default)
    {
        _db.GameSessions.Update(session);
        await _db.SaveChangesAsync(ct);
    }
}
