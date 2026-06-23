using CubeRacing.Application.Config;
using CubeRacing.Application.UseCases;
using CubeRacing.Domain.Entities;
using CubeRacing.Domain.Enums;
using CubeRacing.Infrastructure.Persistence;
using CubeRacing.Infrastructure.Persistence.Repositories;
using CubeRacing.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace CubeRacing.Tests.UseCases;

public class PlaceBetTests
{
    private static (PlaceBet useCase, AppDbContext db) BuildSut()
    {
        var db = TestDbContextFactory.Create();
        var playerRepo = new PlayerRepository(db);
        var sessionRepo = new GameSessionRepository(db);
        var betRepo = new BetRepository(db);
        var hubMock = new Mock<CubeRacing.Application.Interfaces.IGameHubNotifier>();
        hubMock.Setup(h => h.NotifyOddsUpdatedAsync(It.IsAny<Guid>(), It.IsAny<object>()))
               .Returns(Task.CompletedTask);
        var settings = Options.Create(new GameSettings());
        var useCase = new PlaceBet(sessionRepo, betRepo, playerRepo, hubMock.Object, db, settings);
        return (useCase, db);
    }

    private static async Task<(Player player, GameSession session)> SeedAsync(AppDbContext db)
    {
        var player = Player.Create("Tester", 1000);
        db.Players.Add(player);

        var session = GameSession.CreateNew();
        session.StartBetting(60);
        db.GameSessions.Add(session);

        await db.SaveChangesAsync();
        return (player, session);
    }

    [Fact]
    public async Task SuccessfulBet_DeductsChipsAndCreatesRecord()
    {
        var (sut, db) = BuildSut();
        var (player, session) = await SeedAsync(db);

        var result = await sut.ExecuteAsync(new PlaceBetRequest(player.Id, session.Id, 1, 200));

        result.Success.Should().BeTrue();
        db.Bets.Should().HaveCount(1);
        db.Players.Find(player.Id)!.ChipsBalance.Should().Be(800);
    }

    [Fact]
    public async Task DuplicateBet_ReturnAlreadyBetError()
    {
        var (sut, db) = BuildSut();
        var (player, session) = await SeedAsync(db);

        await sut.ExecuteAsync(new PlaceBetRequest(player.Id, session.Id, 1, 100));
        var result = await sut.ExecuteAsync(new PlaceBetRequest(player.Id, session.Id, 2, 100));

        result.Success.Should().BeFalse();
        result.Error.Should().Be(PlaceBetError.AlreadyBet);
        db.Bets.Should().HaveCount(1);
    }

    [Fact]
    public async Task InsufficientChips_ReturnsError()
    {
        var (sut, db) = BuildSut();
        var (player, session) = await SeedAsync(db);

        var result = await sut.ExecuteAsync(new PlaceBetRequest(player.Id, session.Id, 1, 9999));

        result.Success.Should().BeFalse();
        result.Error.Should().Be(PlaceBetError.InsufficientChips);
    }

    [Fact]
    public async Task BettingClosed_ReturnsError()
    {
        var (sut, db) = BuildSut();
        var player = Player.Create("Tester", 1000);
        db.Players.Add(player);

        var session = GameSession.CreateNew();
        session.StartBetting(-1); // deadline already passed
        db.GameSessions.Add(session);
        await db.SaveChangesAsync();

        var result = await sut.ExecuteAsync(new PlaceBetRequest(player.Id, session.Id, 1, 100));

        result.Success.Should().BeFalse();
        result.Error.Should().Be(PlaceBetError.BettingClosed);
    }

    [Fact]
    public async Task InvalidNpcId_ReturnsError()
    {
        var (sut, db) = BuildSut();
        var (player, session) = await SeedAsync(db);

        var result = await sut.ExecuteAsync(new PlaceBetRequest(player.Id, session.Id, 99, 100));

        result.Success.Should().BeFalse();
        result.Error.Should().Be(PlaceBetError.InvalidNpcId);
    }

    [Fact]
    public async Task ZeroAmount_ReturnsInvalidAmountError()
    {
        var (sut, db) = BuildSut();
        var (player, session) = await SeedAsync(db);

        var result = await sut.ExecuteAsync(new PlaceBetRequest(player.Id, session.Id, 1, 0));

        result.Success.Should().BeFalse();
        result.Error.Should().Be(PlaceBetError.InvalidAmount);
    }
}
