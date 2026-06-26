using System.Reflection;
using System.Text.Json;
using CubeRacing.Application.Config;
using CubeRacing.Application.Interfaces;
using CubeRacing.Domain.Entities;
using CubeRacing.Domain.Events;
using CubeRacing.Domain.Interfaces;
using CubeRacing.Infrastructure.Messaging.Consumers;
using CubeRacing.Infrastructure.Persistence.Repositories;
using CubeRacing.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;

namespace CubeRacing.Tests.Consumers;

public class BettingEndedConsumerTests
{
    private static async Task InvokeHandleAsync(BettingEndedConsumer consumer, string json)
    {
        var method = typeof(BettingEndedConsumer)
            .GetMethod("HandleAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(consumer, [json, CancellationToken.None])!;
    }

    private static async Task<(
        BettingEndedConsumer consumer,
        Guid sessionId,
        Mock<ICurrentSessionStore> mockStore,
        Mock<IGameHubNotifier> mockHubNotifier)>
    BuildConsumerAsync(GameSettings settings)
    {
        var db          = TestDbContextFactory.Create();
        var session     = GameSession.CreateNew(settings.MapLength);
        session.StartBetting(60);
        var sessionRepo = new GameSessionRepository(db);
        await sessionRepo.AddAsync(session);
        var roundRepo   = new GameRoundRepository(db);

        var mockHubNotifier = new Mock<IGameHubNotifier>();
        mockHubNotifier.Setup(h => h.NotifyBettingEndedAsync(It.IsAny<Guid>()))
            .Returns(Task.CompletedTask);
        mockHubNotifier.Setup(h => h.NotifyRaceStartingAsync(It.IsAny<Guid>(), It.IsAny<DateTime>()))
            .Returns(Task.CompletedTask);

        var mockStore = new Mock<ICurrentSessionStore>();
        mockStore.SetupProperty(s => s.RaceStartsAt);

        var mockSp = new Mock<IServiceProvider>();
        mockSp.Setup(sp => sp.GetService(typeof(IGameSessionRepository))).Returns(sessionRepo);
        mockSp.Setup(sp => sp.GetService(typeof(IGameRoundRepository))).Returns(roundRepo);
        mockSp.Setup(sp => sp.GetService(typeof(IGameHubNotifier))).Returns(mockHubNotifier.Object);

        var mockScope = new Mock<IServiceScope>();
        mockScope.Setup(s => s.ServiceProvider).Returns(mockSp.Object);

        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        var mockPublisher = new Mock<IMessagePublisher>();
        mockPublisher
            .Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<RoundExecutedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mockPublisher
            .Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<RaceCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var consumer = new BettingEndedConsumer(
            new Mock<IConnectionFactory>().Object,
            mockScopeFactory.Object,
            mockPublisher.Object,
            mockStore.Object,
            Options.Create(settings));

        return (consumer, session.Id, mockStore, mockHubNotifier);
    }

    [Fact]
    public async Task HandleAsync_SetsRaceStartsAtOnStore_BeforeDelay()
    {
        // With RaceStartDelaySeconds = 0, raceStartsAt ≈ UtcNow at the moment HandleAsync runs.
        var settings = new GameSettings
        {
            NpcCount              = 1,
            MapLength             = 1,
            RaceStartDelaySeconds = 0,
            RoundIntervalMs       = 0,
        };
        var (consumer, sessionId, mockStore, _) = await BuildConsumerAsync(settings);
        var json = JsonSerializer.Serialize(new BettingEndedEvent(sessionId));

        var before = DateTime.UtcNow;
        await InvokeHandleAsync(consumer, json);
        var after = DateTime.UtcNow;

        mockStore.Object.RaceStartsAt.Should().NotBeNull(
            "BettingEndedConsumer must set ICurrentSessionStore.RaceStartsAt before the race starts");
        mockStore.Object.RaceStartsAt!.Value
            .Should().BeOnOrAfter(before.AddSeconds(-1))
            .And.BeOnOrBefore(after.AddSeconds(settings.RaceStartDelaySeconds + 1));
    }

    [Fact]
    public async Task HandleAsync_NotifiesRaceStartingBeforeBettingEnded()
    {
        // Use RaceStartDelaySeconds = 0 to avoid wall-clock waits.
        var settings = new GameSettings
        {
            NpcCount              = 1,
            MapLength             = 1,
            RaceStartDelaySeconds = 0,
            RoundIntervalMs       = 0,
        };
        var (consumer, sessionId, _, mockHubNotifier) = await BuildConsumerAsync(settings);
        var json = JsonSerializer.Serialize(new BettingEndedEvent(sessionId));

        await InvokeHandleAsync(consumer, json);

        mockHubNotifier.Verify(
            h => h.NotifyBettingEndedAsync(sessionId),
            Times.Once,
            "NotifyBettingEndedAsync must be called exactly once after betting ends");
        mockHubNotifier.Verify(
            h => h.NotifyRaceStartingAsync(sessionId, It.IsAny<DateTime>()),
            Times.Once,
            "NotifyRaceStartingAsync must be called exactly once with the computed race start time");
    }
}
