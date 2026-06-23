using CubeRacing.Application.Config;
using CubeRacing.Application.Interfaces;
using CubeRacing.Domain.Entities;
using CubeRacing.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CubeRacing.Infrastructure.Services;

public class GameSessionManager : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICurrentSessionStore _store;
    private readonly IMessagePublisher _publisher;
    private readonly ISessionCompletionSignal _signal;
    private readonly GameSettings _settings;

    public GameSessionManager(IServiceScopeFactory scopeFactory, ICurrentSessionStore store,
        IMessagePublisher publisher, ISessionCompletionSignal signal, IOptions<GameSettings> settings)
    {
        _scopeFactory = scopeFactory;
        _store = store;
        _publisher = publisher;
        _signal = signal;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await RunSessionLifecycleAsync(ct);
        }
    }

    private async Task RunSessionLifecycleAsync(CancellationToken ct)
    {
        // Create session
        using var scope = _scopeFactory.CreateScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IGameSessionRepository>();

        var session = GameSession.CreateNew(_settings.MapLength);
        await sessionRepo.AddAsync(session, ct);
        _store.Set(session.Id);

        // Waiting phase
        await Task.Delay(TimeSpan.FromSeconds(_settings.WaitingDurationSeconds), ct);

        // Betting phase
        session.StartBetting(_settings.BettingDurationSeconds);
        await sessionRepo.UpdateAsync(session, ct);

        var remaining = session.BettingDeadline - DateTime.UtcNow;
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, ct);

        // Trigger race
        await _publisher.PublishAsync("betting.ended",
            new CubeRacing.Domain.Events.BettingEndedEvent(session.Id), ct);

        // Wait for settlement to complete (SettlementDoneConsumer signals this)
        await _signal.WaitAsync(ct);
    }
}
