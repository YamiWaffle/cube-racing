using CubeRacing.Application.Interfaces;
using CubeRacing.Domain.Events;
using CubeRacing.Infrastructure.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CubeRacing.Infrastructure.Services;

public class GameHubNotifier : IGameHubNotifier
{
    private readonly IHubContext<GameHub> _hub;
    public GameHubNotifier(IHubContext<GameHub> hub) => _hub = hub;

    public Task NotifyOddsUpdatedAsync(Guid sessionId, object odds)
        => _hub.Clients.Group(sessionId.ToString()).SendAsync("OddsUpdated", odds);

    public Task NotifyBettingEndedAsync(Guid sessionId)
        => _hub.Clients.Group(sessionId.ToString()).SendAsync("BettingEnded");

    public Task NotifyRoundExecutedAsync(Guid sessionId, RoundExecutedEvent round)
        => _hub.Clients.Group(sessionId.ToString()).SendAsync("RoundExecuted", round);

    public Task NotifyRaceCompletedAsync(Guid sessionId, int winnerNpcId)
        => _hub.Clients.Group(sessionId.ToString()).SendAsync("RaceCompleted", new { winnerNpcId });

    public Task NotifySettlementDoneAsync(Guid sessionId, object result)
        => _hub.Clients.Group(sessionId.ToString()).SendAsync("SettlementDone", result);
}
