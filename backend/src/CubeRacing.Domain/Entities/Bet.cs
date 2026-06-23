namespace CubeRacing.Domain.Entities;

public class Bet
{
    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public Guid SessionId { get; private set; }
    public int NpcId { get; private set; }
    public int Amount { get; private set; }
    public int? WinAmount { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private Bet() { }

    public static Bet Create(Guid playerId, Guid sessionId, int npcId, int amount) => new()
    {
        Id = Guid.NewGuid(), PlayerId = playerId, SessionId = sessionId,
        NpcId = npcId, Amount = amount, CreatedAt = DateTime.UtcNow
    };

    public void SetWinAmount(int amount) => WinAmount = amount;
}
