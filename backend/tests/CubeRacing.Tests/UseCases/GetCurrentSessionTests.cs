using CubeRacing.Application.Config;
using CubeRacing.Application.UseCases;
using CubeRacing.Domain.Entities;
using CubeRacing.Infrastructure.Persistence;
using CubeRacing.Infrastructure.Persistence.Repositories;
using CubeRacing.Infrastructure.Services;
using CubeRacing.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace CubeRacing.Tests.UseCases;

public class GetCurrentSessionTests
{
    [Fact]
    public async Task ExecuteAsync_IncludesRaceStartsAt_WhenStoreHasValue()
    {
        var db          = TestDbContextFactory.Create();
        var sessionRepo = new GameSessionRepository(db);
        var betRepo     = new BetRepository(db);
        var store       = new CurrentSessionStore();
        var settings    = Options.Create(new GameSettings { NpcCount = 4 });

        var session = GameSession.CreateNew(20);
        session.StartBetting(60);
        session.StartRacing();
        await sessionRepo.AddAsync(session, default);
        store.Set(session.Id);

        var raceStartsAt = DateTime.UtcNow.AddSeconds(25);
        store.RaceStartsAt = raceStartsAt;

        var useCase = new GetCurrentSession(store, sessionRepo, betRepo, settings);
        var result  = await useCase.ExecuteAsync();

        result.Should().NotBeNull();
        result!.RaceStartsAt.Should().BeCloseTo(raceStartsAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNullRaceStartsAt_WhenStoreIsEmpty()
    {
        var db          = TestDbContextFactory.Create();
        var sessionRepo = new GameSessionRepository(db);
        var betRepo     = new BetRepository(db);
        var store       = new CurrentSessionStore();
        var settings    = Options.Create(new GameSettings { NpcCount = 4 });

        var session = GameSession.CreateNew(20);
        session.StartBetting(60);
        await sessionRepo.AddAsync(session, default);
        store.Set(session.Id);
        // store.RaceStartsAt not set → should be null

        var useCase = new GetCurrentSession(store, sessionRepo, betRepo, settings);
        var result  = await useCase.ExecuteAsync();

        result.Should().NotBeNull();
        result!.RaceStartsAt.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_IncludesBettingStartsAt_WhenStoreHasValue()
    {
        var db          = TestDbContextFactory.Create();
        var sessionRepo = new GameSessionRepository(db);
        var betRepo     = new BetRepository(db);
        var store       = new CurrentSessionStore();
        var settings    = Options.Create(new GameSettings { NpcCount = 4 });

        var session = GameSession.CreateNew(20);
        await sessionRepo.AddAsync(session, default);
        store.Set(session.Id);

        var bettingStartsAt = DateTime.UtcNow.AddSeconds(5);
        store.BettingStartsAt = bettingStartsAt;

        var useCase = new GetCurrentSession(store, sessionRepo, betRepo, settings);
        var result  = await useCase.ExecuteAsync();

        result.Should().NotBeNull();
        result!.BettingStartsAt.Should().BeCloseTo(bettingStartsAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNullBettingStartsAt_WhenStoreIsEmpty()
    {
        var db          = TestDbContextFactory.Create();
        var sessionRepo = new GameSessionRepository(db);
        var betRepo     = new BetRepository(db);
        var store       = new CurrentSessionStore();
        var settings    = Options.Create(new GameSettings { NpcCount = 4 });

        var session = GameSession.CreateNew(20);
        await sessionRepo.AddAsync(session, default);
        store.Set(session.Id);
        // store.BettingStartsAt not set → should be null

        var useCase = new GetCurrentSession(store, sessionRepo, betRepo, settings);
        var result  = await useCase.ExecuteAsync();

        result.Should().NotBeNull();
        result!.BettingStartsAt.Should().BeNull();
    }
}
