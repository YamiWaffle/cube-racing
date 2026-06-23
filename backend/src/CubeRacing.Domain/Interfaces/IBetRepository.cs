using CubeRacing.Domain.Entities;
namespace CubeRacing.Domain.Interfaces;

public interface IBetRepository
{
    Task<bool> ExistsAsync(Guid playerId, Guid sessionId, CancellationToken ct = default);
    Task AddAsync(Bet bet, CancellationToken ct = default);
    Task<List<Bet>> GetBySessionAsync(Guid sessionId, CancellationToken ct = default);
    Task UpdateAsync(Bet bet, CancellationToken ct = default);
}
