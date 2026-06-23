using CubeRacing.Domain.Entities;

namespace CubeRacing.Application.GameEngine;

public record BetResult(Guid BetId, int WinAmount);

public class SettlementCalculator
{
    public List<BetResult> Calculate(int winnerNpcId, IEnumerable<Bet> bets)
    {
        var list = bets.ToList();
        int totalPool = list.Sum(b => b.Amount);
        int poolOnWinner = list.Where(b => b.NpcId == winnerNpcId).Sum(b => b.Amount);

        if (poolOnWinner == 0)
            return list.Select(b => new BetResult(b.Id, 0)).ToList();

        double odds = (double)totalPool / poolOnWinner;
        return list.Select(b =>
        {
            if (b.NpcId != winnerNpcId) return new BetResult(b.Id, 0);
            return new BetResult(b.Id, (int)Math.Floor(b.Amount * odds));
        }).ToList();
    }
}
