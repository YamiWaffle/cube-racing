using CubeRacing.Application.Interfaces;

namespace CubeRacing.Infrastructure.Services;

public class CurrentSessionStore : ICurrentSessionStore
{
    private Guid? _id;
    public Guid?     CurrentSessionId => _id;
    public DateTime? RaceStartsAt     { get; set; }
    public void Set(Guid sessionId) => _id = sessionId;
}
