using CubeRacing.Application.GameEngine;
using CubeRacing.Domain.Entities;
using FluentAssertions;

namespace CubeRacing.Tests.GameEngine;

public class SettlementCalculatorTests
{
    private static Bet MakeBet(int npcId, int amount)
        => Bet.Create(Guid.NewGuid(), Guid.NewGuid(), npcId, amount);

    [Fact]
    public void WinnerReceivesEntirePool()
    {
        var bets = new[] { MakeBet(1, 100), MakeBet(2, 100) };
        var calc = new SettlementCalculator();

        var results = calc.Calculate(winnerNpcId: 1, bets);

        results.First(r => r.BetId == bets[0].Id).WinAmount.Should().Be(200);
        results.First(r => r.BetId == bets[1].Id).WinAmount.Should().Be(0);
    }

    [Fact]
    public void MultipleWinnersSharePoolByProportion()
    {
        // NPC1 wins; player A bet 100, player B bet 300 on NPC1; player C bet 200 on NPC2
        var betA = MakeBet(1, 100);
        var betB = MakeBet(1, 300);
        var betC = MakeBet(2, 200);
        var calc = new SettlementCalculator();

        var results = calc.Calculate(winnerNpcId: 1, [betA, betB, betC]);

        // totalPool=600, poolOnWinner=400, odds=1.5
        results.First(r => r.BetId == betA.Id).WinAmount.Should().Be(150); // 100 * 1.5
        results.First(r => r.BetId == betB.Id).WinAmount.Should().Be(450); // 300 * 1.5
        results.First(r => r.BetId == betC.Id).WinAmount.Should().Be(0);
    }

    [Fact]
    public void NobodyBetOnWinner_AllGetZero()
    {
        var bets = new[] { MakeBet(2, 100), MakeBet(3, 200) };
        var calc = new SettlementCalculator();

        var results = calc.Calculate(winnerNpcId: 1, bets);

        results.Should().OnlyContain(r => r.WinAmount == 0);
    }

    [Fact]
    public void EmptyBetList_ReturnsEmpty()
    {
        var calc = new SettlementCalculator();
        calc.Calculate(1, []).Should().BeEmpty();
    }

    [Fact]
    public void WinAmountUsesFloorDivision()
    {
        // totalPool=3, poolOnWinner=2, odds=1.5; betA(NPC1,amount=1) → floor(1*1.5)=1
        var betA = MakeBet(1, 1); // NPC1, amount=1 → winner
        var betB = MakeBet(1, 1); // NPC1, amount=1 → winner; poolOnWinner=2
        var betC = MakeBet(2, 1); // NPC2, amount=1 → loser; totalPool=3
        var calc = new SettlementCalculator();

        var results = calc.Calculate(winnerNpcId: 1, [betA, betB, betC]);

        results.First(r => r.BetId == betA.Id).WinAmount.Should().Be(1);
    }
}
