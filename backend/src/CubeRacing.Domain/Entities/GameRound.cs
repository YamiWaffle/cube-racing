namespace CubeRacing.Domain.Entities;

public class GameRound
{
    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public int RoundNumber { get; private set; }
    public string MovementDataJson { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }

    private GameRound() { }

    public static GameRound Create(Guid sessionId, int roundNumber, string json) => new()
    {
        Id = Guid.NewGuid(), SessionId = sessionId,
        RoundNumber = roundNumber, MovementDataJson = json,
        CreatedAt = DateTime.UtcNow
    };
}
