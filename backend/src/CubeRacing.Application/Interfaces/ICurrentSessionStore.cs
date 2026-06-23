namespace CubeRacing.Application.Interfaces;

public interface ICurrentSessionStore
{
    Guid? CurrentSessionId { get; }
    void Set(Guid sessionId);
}
