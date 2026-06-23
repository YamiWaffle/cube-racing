using System.Text.Json;
using CubeRacing.Application.Interfaces;
using CubeRacing.Domain.Events;
using RabbitMQ.Client;

namespace CubeRacing.Infrastructure.Messaging.Consumers;

public class SettlementDoneConsumer : RabbitMqConsumerBase
{
    private readonly ISessionCompletionSignal _signal;

    public SettlementDoneConsumer(IConnectionFactory factory, ISessionCompletionSignal signal)
        : base(factory, "settlement.done")
        => _signal = signal;

    protected override Task HandleAsync(string json, CancellationToken ct)
    {
        _signal.Signal();
        return Task.CompletedTask;
    }
}
