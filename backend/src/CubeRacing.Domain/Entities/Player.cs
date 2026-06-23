namespace CubeRacing.Domain.Entities;

public class Player
{
    public Guid Id { get; private set; }
    public string Nickname { get; private set; } = string.Empty;
    public Guid Token { get; private set; }
    public int ChipsBalance { get; private set; }
    public int TotalChipsWon { get; private set; }
    public int CorrectBets { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private Player() { }

    public static Player Create(string nickname, int initialChips) => new()
    {
        Id = Guid.NewGuid(), Nickname = nickname, Token = Guid.NewGuid(),
        ChipsBalance = initialChips, CreatedAt = DateTime.UtcNow
    };

    public void DeductChips(int amount) => ChipsBalance -= amount;

    public void AddWinnings(int winAmount)
    {
        ChipsBalance += winAmount;
        TotalChipsWon += winAmount;
        CorrectBets++;
    }
}
