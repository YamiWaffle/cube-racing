namespace CubeRacing.Domain.Events;
public record RaceCompletedEvent(Guid SessionId, int WinnerNpcId);
