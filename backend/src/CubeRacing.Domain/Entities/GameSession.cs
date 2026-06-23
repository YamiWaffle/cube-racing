using CubeRacing.Domain.Enums;
namespace CubeRacing.Domain.Entities;

public class GameSession
{
    public Guid Id { get; private set; }
    public GameStatus Status { get; private set; }
    public DateTime BettingDeadline { get; private set; }
    public int MapLength { get; private set; }
    public int? WinnerNpcId { get; private set; }
    public int TotalPool { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private GameSession() { }

    public static GameSession CreateNew(int mapLength = 20) => new()
    {
        Id = Guid.NewGuid(), Status = GameStatus.Waiting,
        MapLength = mapLength, CreatedAt = DateTime.UtcNow
    };

    public void StartBetting(int durationSeconds)
    {
        Status = GameStatus.Betting;
        BettingDeadline = DateTime.UtcNow.AddSeconds(durationSeconds);
    }

    public void StartRacing() => Status = GameStatus.Racing;

    public void Complete(int winnerNpcId)
    {
        WinnerNpcId = winnerNpcId;
        Status = GameStatus.Completed;
    }

    public void AddToPool(int amount) => TotalPool += amount;
}
