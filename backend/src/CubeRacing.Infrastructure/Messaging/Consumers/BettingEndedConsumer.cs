using System.Text.Json;
using CubeRacing.Application.Config;
using CubeRacing.Application.GameEngine;
using CubeRacing.Application.Interfaces;
using CubeRacing.Domain.Entities;
using CubeRacing.Domain.Events;
using CubeRacing.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace CubeRacing.Infrastructure.Messaging.Consumers;

public class BettingEndedConsumer : RabbitMqConsumerBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMessagePublisher _publisher;
    private readonly GameSettings _settings;

    public BettingEndedConsumer(IConnectionFactory factory, IServiceScopeFactory scopeFactory,
        IMessagePublisher publisher, IOptions<GameSettings> settings)
        : base(factory, "betting.ended")
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _settings = settings.Value;
    }

    protected override async Task HandleAsync(string json, CancellationToken ct)
    {
        var ev = JsonSerializer.Deserialize<BettingEndedEvent>(json)!;

        using var scope = _scopeFactory.CreateScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IGameSessionRepository>();
        var roundRepo = scope.ServiceProvider.GetRequiredService<IGameRoundRepository>();
        var hubNotifier = scope.ServiceProvider.GetRequiredService<IGameHubNotifier>();

        var session = await sessionRepo.GetByIdAsync(ev.SessionId, ct);
        if (session is null) return;

        session.StartRacing();
        await sessionRepo.UpdateAsync(session, ct);
        await hubNotifier.NotifyBettingEndedAsync(ev.SessionId);

        var simulator = new RaceSimulator(_settings.NpcCount, _settings.MapLength);
        int roundNumber = 0;

        while (simulator.GetWinner() is null)
        {
            roundNumber++;
            var result = simulator.SimulateRound();

            var round = GameRound.Create(ev.SessionId, roundNumber, JsonSerializer.Serialize(result));
            await roundRepo.AddAsync(round, ct);

            var roundEvent = new RoundExecutedEvent(
                ev.SessionId, roundNumber,
                result.Actions.Select(a => new RoundActionDto(a.NpcId, a.DiceRoll, a.FromSquare, a.ToSquare, a.CarriedNpcIds)).ToList(),
                result.SquareStacks,
                result.Winner);

            await _publisher.PublishAsync("round.executed", roundEvent, ct);

            if (result.Winner is null)
                await Task.Delay(_settings.RoundIntervalMs, ct);
        }

        await _publisher.PublishAsync("race.completed",
            new RaceCompletedEvent(ev.SessionId, simulator.GetWinner()!.Value), ct);
    }
}
