using CubeRacing.Application.Config;
using CubeRacing.Domain.Entities;
using CubeRacing.Domain.Interfaces;
using Microsoft.Extensions.Options;

namespace CubeRacing.Application.UseCases;

public record CreatePlayerRequest(string Nickname);
public record CreatePlayerResponse(Guid PlayerId, Guid Token, int ChipsBalance);

public class CreatePlayer
{
    private readonly IPlayerRepository _players;
    private readonly GameSettings _settings;

    public CreatePlayer(IPlayerRepository players, IOptions<GameSettings> settings)
    {
        _players = players;
        _settings = settings.Value;
    }

    public async Task<CreatePlayerResponse> ExecuteAsync(CreatePlayerRequest req, CancellationToken ct = default)
    {
        var player = Player.Create(req.Nickname.Trim(), _settings.InitialChips);
        await _players.AddAsync(player, ct);
        return new CreatePlayerResponse(player.Id, player.Token, player.ChipsBalance);
    }
}
