using CubeRacing.Application.GameEngine;
using CubeRacing.Tests.Helpers;
using FluentAssertions;

namespace CubeRacing.Tests.GameEngine;

public class RaceSimulatorTests
{
    [Fact]
    public void AllNpcsStartNotAtSquareZero()
    {
        var sim = new RaceSimulator(4, 20);
        sim.GetSquareStacks().Should().BeEmpty();
    }

    [Fact]
    public void NpcMovesForwardByDiceAmount()
    {
        var rand = new FixedRaceRandomizer(order: [1], dice: [2]);
        var sim = new RaceSimulator(1, 20, rand);

        var result = sim.SimulateRound();

        result.Actions.Should().HaveCount(1);
        result.Actions[0].NpcId.Should().Be(1);
        result.Actions[0].FromSquare.Should().Be(-1);
        result.Actions[0].ToSquare.Should().Be(1);
        result.Actions[0].DiceRoll.Should().Be(2);
    }

    [Fact]
    public void LaterArrivingNpcStacksOnTop()
    {
        // NPC1 already at sq3; NPC2 moves from sq0 rolling 3 → lands on sq3 on top of NPC1
        var rand = new FixedRaceRandomizer(order: [2, 1], dice: [3, 1]);
        var sim = RaceSimulator.CreateWithPositions(
            new Dictionary<int, List<int>> { [3] = [1], [0] = [2] },
            mapLength: 20, rand);

        sim.SimulateRound();

        // NPC2 moves to sq3 → stack becomes [1,2]; then NPC1 (bottom) moves +1 → sq4 with NPC2
        sim.GetSquareStacks().Should().ContainKey("4");
        sim.GetSquareStacks()["4"].Should().Equal([1, 2]);
    }

    [Fact]
    public void BottomNpcCarriesEntireStackAboveIt()
    {
        // NPC1 (bottom) moves first, carrying NPC2+NPC3 to sq7.
        // Then NPC2 and NPC3 each take their own turn from their new positions.
        var rand = new FixedRaceRandomizer(order: [1, 2, 3], dice: [2, 1, 1]);
        var sim = RaceSimulator.CreateWithPositions(
            new Dictionary<int, List<int>> { [5] = [1, 2, 3] },
            mapLength: 20, rand);

        var result = sim.SimulateRound();

        result.Actions.Should().HaveCount(3);
        var action = result.Actions[0];
        action.NpcId.Should().Be(1);
        action.FromSquare.Should().Be(5);
        action.ToSquare.Should().Be(7);
        action.CarriedNpcIds.Should().Equal([2, 3]);
        // After NPC2 (+1→sq8) and NPC3 (+1→sq9) take their own turns:
        result.SquareStacks["7"].Should().Equal([1]);
        result.SquareStacks["8"].Should().Equal([2]);
        result.SquareStacks["9"].Should().Equal([3]);
    }

    [Fact]
    public void TopNpcMovesAloneWithoutCarryingNpcsBelow()
    {
        // NPC2 (top) moves first — carries nobody. Then NPC1 (bottom) takes its own turn.
        var rand = new FixedRaceRandomizer(order: [2, 1], dice: [3, 1]);
        var sim = RaceSimulator.CreateWithPositions(
            new Dictionary<int, List<int>> { [5] = [1, 2] },
            mapLength: 20, rand);

        var result = sim.SimulateRound();

        result.Actions.Should().HaveCount(2);
        var action = result.Actions[0]; // NPC2 moves first
        action.NpcId.Should().Be(2);
        action.CarriedNpcIds.Should().BeEmpty();
        // NPC1 also takes its own turn (+1 → sq6), so sq5 is empty at end
        result.SquareStacks.Should().NotContainKey("5");
        result.SquareStacks["6"].Should().Equal([1]);
        result.SquareStacks["8"].Should().Equal([2]);
    }

    [Fact]
    public void WinnerIsTopmostNpcWhenStackReachesFinish()
    {
        var rand = new FixedRaceRandomizer(order: [1], dice: [1]);
        var sim = RaceSimulator.CreateWithPositions(
            new Dictionary<int, List<int>> { [19] = [1, 2] }, // 2 on top
            mapLength: 20, rand);

        var result = sim.SimulateRound();

        result.Winner.Should().Be(2); // NPC2 is topmost
    }

    [Fact]
    public void NpcDoesNotExceedFinishSquare()
    {
        var rand = new FixedRaceRandomizer(order: [1], dice: [5]);
        var sim = RaceSimulator.CreateWithPositions(
            new Dictionary<int, List<int>> { [16] = [1] },
            mapLength: 20, rand);

        var result = sim.SimulateRound();

        result.Actions[0].ToSquare.Should().Be(19);
    }

    [Fact]
    public void GetWinnerReturnsNullWhenNobodyAtFinish()
    {
        var sim = new RaceSimulator(4, 20);
        sim.GetWinner().Should().BeNull();
    }

}
