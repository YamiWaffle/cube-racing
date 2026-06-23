using CubeRacing.Domain.Entities;
namespace CubeRacing.Domain.Interfaces;

public interface IGameSessionRepository
{
    Task<GameSession?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(GameSession session, CancellationToken ct = default);
    Task UpdateAsync(GameSession session, CancellationToken ct = default);
}
