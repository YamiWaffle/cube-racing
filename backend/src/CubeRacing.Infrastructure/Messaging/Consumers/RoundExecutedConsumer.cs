using System.Text.Json;
using CubeRacing.Application.Interfaces;
using CubeRacing.Domain.Events;
using RabbitMQ.Client;

namespace CubeRacing.Infrastructure.Messaging.Consumers;

public class RoundExecutedConsumer : RabbitMqConsumerBase
{
    private readonly IGameHubNotifier _hub;

    public RoundExecutedConsumer(IConnectionFactory factory, IGameHubNotifier hub)
        : base(factory, "round.executed")
        => _hub = hub;

    protected override async Task HandleAsync(string json, CancellationToken ct)
    {
        var ev = JsonSerializer.Deserialize<RoundExecutedEvent>(json)!;
        await _hub.NotifyRoundExecutedAsync(ev.SessionId, ev);
    }
}
