using CubeRacing.Domain.Events;

namespace CubeRacing.Application.Interfaces;

public interface IGameHubNotifier
{
    Task NotifyBettingStartedAsync(Guid sessionId);
    Task NotifyOddsUpdatedAsync(Guid sessionId, object odds);
    Task NotifyBettingEndedAsync(Guid sessionId);
    Task NotifyRoundExecutedAsync(Guid sessionId, RoundExecutedEvent round);
    Task NotifyRaceCompletedAsync(Guid sessionId, int winnerNpcId);
    Task NotifySettlementDoneAsync(Guid sessionId, object result);
}
