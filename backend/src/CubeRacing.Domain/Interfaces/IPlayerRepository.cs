using CubeRacing.Domain.Entities;
namespace CubeRacing.Domain.Interfaces;

public interface IPlayerRepository
{
    Task<Player?> GetByTokenAsync(Guid token, CancellationToken ct = default);
    Task<Player?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Player player, CancellationToken ct = default);
    Task UpdateAsync(Player player, CancellationToken ct = default);
    Task<List<Player>> GetTopByWinningsAsync(int count, CancellationToken ct = default);
}
