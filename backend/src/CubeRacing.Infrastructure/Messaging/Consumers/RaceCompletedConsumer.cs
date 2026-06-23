using System.Text.Json;
using CubeRacing.Application.Interfaces;
using CubeRacing.Application.UseCases;
using CubeRacing.Domain.Events;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace CubeRacing.Infrastructure.Messaging.Consumers;

public class RaceCompletedConsumer : RabbitMqConsumerBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMessagePublisher _publisher;
    private readonly IGameHubNotifier _hub;

    public RaceCompletedConsumer(IConnectionFactory factory, IServiceScopeFactory scopeFactory,
        IMessagePublisher publisher, IGameHubNotifier hub)
        : base(factory, "race.completed")
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _hub = hub;
    }

    protected override async Task HandleAsync(string json, CancellationToken ct)
    {
        var ev = JsonSerializer.Deserialize<RaceCompletedEvent>(json)!;

        using var scope = _scopeFactory.CreateScope();
        var settle = scope.ServiceProvider.GetRequiredService<SettleSession>();
        var result = await settle.ExecuteAsync(ev.SessionId, ev.WinnerNpcId, ct);

        await _hub.NotifyRaceCompletedAsync(ev.SessionId, ev.WinnerNpcId);
        await _hub.NotifySettlementDoneAsync(ev.SessionId, result);
        await _publisher.PublishAsync("settlement.done", new SettlementDoneEvent(ev.SessionId), ct);
    }
}
