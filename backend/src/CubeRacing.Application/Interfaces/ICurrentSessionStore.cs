namespace CubeRacing.Application.Interfaces;

public interface ICurrentSessionStore
{
    Guid?     CurrentSessionId { get; }
    DateTime? RaceStartsAt     { get; set; }
    void Set(Guid sessionId);
}
