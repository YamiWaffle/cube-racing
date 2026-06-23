using CubeRacing.Domain.Entities;
namespace CubeRacing.Domain.Interfaces;

public interface IGameRoundRepository
{
    Task AddAsync(GameRound round, CancellationToken ct = default);
}
