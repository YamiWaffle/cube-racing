# Cube Racing Backend — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the .NET 8.0 backend for Cube Racing — real-time NPC racing game with betting, SignalR broadcast, and RabbitMQ phase transitions.

**Architecture:** Single .NET 8.0 Web API across four projects (Domain → Application → Infrastructure → API). A `BackgroundService` (`GameSessionManager`) drives the game lifecycle; RabbitMQ decouples phase transitions; SignalR broadcasts race events to Unity clients.

**Tech Stack:** .NET 8.0 · ASP.NET Core · EF Core 8 · SQL Server · RabbitMQ.Client 6 · SignalR · xUnit · FluentAssertions · Moq

## Global Constraints

- Target framework: `net8.0`
- All IDs: `Guid`; all timestamps: `DateTime.UtcNow`
- Token auth via `Authorization: Bearer {guid}` header — custom middleware, no JWT
- NPC IDs: `int` 1–4; configured in `appsettings.json`
- Chips: `int` (no decimals); win amount uses `(int)Math.Floor(amount * odds)`
- `squareStacks` arrays: **bottom-to-top** (index 0 = bottom, last index = top = winner candidate)
- Map: 20 squares (square 0 = start, square 20 = finish)
- Betting: 60 s; Waiting: 5 s; Round interval: 1500 ms; Initial chips: 1000

---

## File Map

```
backend/
├── docker-compose.yml
├── CubeRacing.sln
├── src/
│   ├── CubeRacing.Domain/
│   │   ├── Enums/GameStatus.cs
│   │   ├── Entities/{Player,GameSession,Bet,GameRound}.cs
│   │   ├── Events/{BettingEndedEvent,RoundExecutedEvent,RaceCompletedEvent,SettlementDoneEvent}.cs
│   │   └── Interfaces/{IPlayerRepository,IGameSessionRepository,IBetRepository,IGameRoundRepository}.cs
│   ├── CubeRacing.Application/
│   │   ├── Config/{GameSettings,NpcConfig}.cs
│   │   ├── Interfaces/{IMessagePublisher,IGameHubNotifier,ICurrentSessionStore,ISessionCompletionSignal}.cs
│   │   ├── Dtos/{NpcOddsDto,SettlementResultDto,LeaderboardEntryDto,CurrentSessionDto}.cs
│   │   ├── GameEngine/{IRaceRandomizer,RaceSimulator,RaceResult,SettlementCalculator}.cs
│   │   └── UseCases/{CreatePlayer,PlaceBet,GetCurrentSession,GetLeaderboard,SettleSession}.cs
│   ├── CubeRacing.Infrastructure/
│   │   ├── Persistence/{AppDbContext,Repositories/…}.cs
│   │   ├── Messaging/{RabbitMqPublisher,RabbitMqConsumerBase}.cs
│   │   ├── Messaging/Consumers/{BettingEndedConsumer,RoundExecutedConsumer,RaceCompletedConsumer,SettlementDoneConsumer}.cs
│   │   ├── Services/{CurrentSessionStore,SessionCompletionSignal,GameSessionManager,GameHubNotifier}.cs
│   │   └── Hubs/GameHub.cs
│   └── CubeRacing.API/
│       ├── Controllers/{PlayersController,SessionsController,LeaderboardController}.cs
│       ├── Middleware/TokenAuthMiddleware.cs
│       └── Program.cs
└── tests/CubeRacing.Tests/
    ├── GameEngine/{RaceSimulatorTests,SettlementCalculatorTests}.cs
    ├── Helpers/{FixedRaceRandomizer,TestDbContextFactory}.cs
    └── UseCases/PlaceBetTests.cs
```

---

### Task 1: Solution Scaffold & Dev Infrastructure

**Files:** `docker-compose.yml`, `CubeRacing.sln`, 5 `.csproj` files, `appsettings.json`

**Interfaces:**
- Produces: solution structure all later tasks reference

- [ ] **Step 1: Write docker-compose.yml**

```yaml
# backend/docker-compose.yml
services:
  mssql:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "CubeRacing!123"
    ports:
      - "1433:1433"
  rabbitmq:
    image: rabbitmq:3-management
    ports:
      - "5672:5672"
      - "15672:15672"
```

- [ ] **Step 2: Start containers**

```bash
cd /Users/waffle/workspace/cube-racing/backend
docker compose up -d
```
Expected: `docker compose ps` shows both services as `running`.

- [ ] **Step 3: Create solution and projects**

```bash
cd /Users/waffle/workspace/cube-racing/backend
dotnet new sln -n CubeRacing
dotnet new classlib -n CubeRacing.Domain      -o src/CubeRacing.Domain      --framework net8.0
dotnet new classlib -n CubeRacing.Application -o src/CubeRacing.Application --framework net8.0
dotnet new classlib -n CubeRacing.Infrastructure -o src/CubeRacing.Infrastructure --framework net8.0
dotnet new webapi   -n CubeRacing.API         -o src/CubeRacing.API         --framework net8.0
dotnet new xunit    -n CubeRacing.Tests       -o tests/CubeRacing.Tests     --framework net8.0

dotnet sln add src/CubeRacing.Domain src/CubeRacing.Application src/CubeRacing.Infrastructure src/CubeRacing.API tests/CubeRacing.Tests
```

- [ ] **Step 4: Add project references**

```bash
dotnet add src/CubeRacing.Application    reference src/CubeRacing.Domain
dotnet add src/CubeRacing.Infrastructure reference src/CubeRacing.Application
dotnet add src/CubeRacing.API           reference src/CubeRacing.Infrastructure
dotnet add src/CubeRacing.API           reference src/CubeRacing.Application
dotnet add tests/CubeRacing.Tests       reference src/CubeRacing.Application
dotnet add tests/CubeRacing.Tests       reference src/CubeRacing.Domain
```

- [ ] **Step 5: Add NuGet packages**

```bash
# Infrastructure
dotnet add src/CubeRacing.Infrastructure package Microsoft.EntityFrameworkCore.SqlServer --version 8.*
dotnet add src/CubeRacing.Infrastructure package Microsoft.EntityFrameworkCore.Tools     --version 8.*
dotnet add src/CubeRacing.Infrastructure package RabbitMQ.Client --version 6.*
dotnet add src/CubeRacing.Infrastructure package Microsoft.AspNetCore.SignalR            --version 1.*

# API
dotnet add src/CubeRacing.API package Microsoft.EntityFrameworkCore.Design --version 8.*
dotnet add src/CubeRacing.API package Swashbuckle.AspNetCore               --version 6.*

# Tests
dotnet add tests/CubeRacing.Tests package FluentAssertions                         --version 6.*
dotnet add tests/CubeRacing.Tests package Moq                                      --version 4.*
dotnet add tests/CubeRacing.Tests package Microsoft.EntityFrameworkCore.InMemory   --version 8.*
```

- [ ] **Step 6: Remove boilerplate**

```bash
rm src/CubeRacing.Domain/Class1.cs
rm src/CubeRacing.Application/Class1.cs
rm src/CubeRacing.Infrastructure/Class1.cs
rm src/CubeRacing.API/WeatherForecast.cs
rm src/CubeRacing.API/Controllers/WeatherForecastController.cs
rm tests/CubeRacing.Tests/UnitTest1.cs
```

- [ ] **Step 7: Write appsettings.json**

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost,1433;Database=CubeRacing;User Id=sa;Password=CubeRacing!123;TrustServerCertificate=True"
  },
  "RabbitMQ": { "Host": "localhost", "Port": 5672, "Username": "guest", "Password": "guest" },
  "GameSettings": {
    "MapLength": 20, "NpcCount": 4,
    "BettingDurationSeconds": 60, "WaitingDurationSeconds": 5,
    "RoundIntervalMs": 1500, "InitialChips": 1000
  },
  "Npcs": [
    { "Id": 1, "Name": "紅方塊", "ColorHex": "#E53E3E" },
    { "Id": 2, "Name": "藍方塊", "ColorHex": "#3182CE" },
    { "Id": 3, "Name": "黃方塊", "ColorHex": "#D69E2E" },
    { "Id": 4, "Name": "綠方塊", "ColorHex": "#38A169" }
  ]
}
```

- [ ] **Step 8: Verify build**

```bash
dotnet build
```
Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 9: Commit**

```bash
git init
git add .
git commit -m "chore: scaffold solution with 4 projects and dev infrastructure"
```

---

### Task 2: Domain Layer

**Files:**
- Create: `src/CubeRacing.Domain/Enums/GameStatus.cs`
- Create: `src/CubeRacing.Domain/Entities/Player.cs`
- Create: `src/CubeRacing.Domain/Entities/GameSession.cs`
- Create: `src/CubeRacing.Domain/Entities/Bet.cs`
- Create: `src/CubeRacing.Domain/Entities/GameRound.cs`
- Create: `src/CubeRacing.Domain/Events/` (4 files)
- Create: `src/CubeRacing.Domain/Interfaces/` (4 files)

**Interfaces:**
- Produces: all domain types referenced by Application, Infrastructure, and Tests

- [ ] **Step 1: Create enum and entities**

```csharp
// src/CubeRacing.Domain/Enums/GameStatus.cs
namespace CubeRacing.Domain.Enums;
public enum GameStatus { Waiting, Betting, Racing, Settling, Completed }
```

```csharp
// src/CubeRacing.Domain/Entities/Player.cs
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
```

```csharp
// src/CubeRacing.Domain/Entities/GameSession.cs
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
```

```csharp
// src/CubeRacing.Domain/Entities/Bet.cs
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
```

```csharp
// src/CubeRacing.Domain/Entities/GameRound.cs
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
```

- [ ] **Step 2: Create domain events**

```csharp
// src/CubeRacing.Domain/Events/BettingEndedEvent.cs
namespace CubeRacing.Domain.Events;
public record BettingEndedEvent(Guid SessionId);

// src/CubeRacing.Domain/Events/RoundExecutedEvent.cs
namespace CubeRacing.Domain.Events;

public record RoundExecutedEvent(
    Guid SessionId,
    int RoundNumber,
    List<RoundActionDto> Actions,
    Dictionary<string, List<int>> SquareStacks,
    int? Winner);

public record RoundActionDto(int NpcId, int DiceRoll, int FromSquare, int ToSquare, List<int> CarriedNpcIds);

// src/CubeRacing.Domain/Events/RaceCompletedEvent.cs
namespace CubeRacing.Domain.Events;
public record RaceCompletedEvent(Guid SessionId, int WinnerNpcId);

// src/CubeRacing.Domain/Events/SettlementDoneEvent.cs
namespace CubeRacing.Domain.Events;
public record SettlementDoneEvent(Guid SessionId);
```

- [ ] **Step 3: Create repository interfaces**

```csharp
// src/CubeRacing.Domain/Interfaces/IPlayerRepository.cs
using CubeRacing.Domain.Entities;
namespace CubeRacing.Domain.Interfaces;

public interface IPlayerRepository
{
    Task<Player?> GetByTokenAsync(Guid token, CancellationToken ct = default);
    Task<Player?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Player player, CancellationToken ct = default);
    Task UpdateAsync(Player player, CancellationToken ct = default);
    Task<List<Player>> GetTopByWinningsAsync(int count, CancellationToken ct = default);
}

// src/CubeRacing.Domain/Interfaces/IGameSessionRepository.cs
using CubeRacing.Domain.Entities;
namespace CubeRacing.Domain.Interfaces;

public interface IGameSessionRepository
{
    Task<GameSession?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(GameSession session, CancellationToken ct = default);
    Task UpdateAsync(GameSession session, CancellationToken ct = default);
}

// src/CubeRacing.Domain/Interfaces/IBetRepository.cs
using CubeRacing.Domain.Entities;
namespace CubeRacing.Domain.Interfaces;

public interface IBetRepository
{
    Task<bool> ExistsAsync(Guid playerId, Guid sessionId, CancellationToken ct = default);
    Task AddAsync(Bet bet, CancellationToken ct = default);
    Task<List<Bet>> GetBySessionAsync(Guid sessionId, CancellationToken ct = default);
    Task UpdateAsync(Bet bet, CancellationToken ct = default);
}

// src/CubeRacing.Domain/Interfaces/IGameRoundRepository.cs
using CubeRacing.Domain.Entities;
namespace CubeRacing.Domain.Interfaces;

public interface IGameRoundRepository
{
    Task AddAsync(GameRound round, CancellationToken ct = default);
}
```

- [ ] **Step 4: Build and commit**

```bash
dotnet build
git add src/CubeRacing.Domain
git commit -m "feat: add domain entities, events, and repository interfaces"
```
Expected: `Build succeeded. 0 Error(s).`

---

### Task 3: RaceSimulator (TDD)

**Files:**
- Create: `src/CubeRacing.Application/GameEngine/IRaceRandomizer.cs`
- Create: `src/CubeRacing.Application/GameEngine/RaceSimulator.cs`
- Create: `tests/CubeRacing.Tests/Helpers/FixedRaceRandomizer.cs`
- Create: `tests/CubeRacing.Tests/GameEngine/RaceSimulatorTests.cs`

**Interfaces:**
- Produces:
  - `RaceSimulator(int npcCount, int mapLength, IRaceRandomizer? randomizer = null)`
  - `static RaceSimulator CreateWithPositions(Dictionary<int, List<int>> positions, int mapLength, IRaceRandomizer randomizer)`
  - `RoundResult SimulateRound()` — simulates one round, returns result
  - `int? GetWinner()` — topmost NPC on finish square, or null
  - `RoundResult { List<RoundAction> Actions, Dictionary<string, List<int>> SquareStacks, int? Winner }`
  - `RoundAction { int NpcId, int DiceRoll, int FromSquare, int ToSquare, List<int> CarriedNpcIds }`

- [ ] **Step 1: Create IRaceRandomizer**

```csharp
// src/CubeRacing.Application/GameEngine/IRaceRandomizer.cs
namespace CubeRacing.Application.GameEngine;

public interface IRaceRandomizer
{
    int RollDice();
    IReadOnlyList<int> ShuffleOrder(IReadOnlyList<int> npcIds);
}

public class DefaultRaceRandomizer : IRaceRandomizer
{
    private readonly Random _rng;
    public DefaultRaceRandomizer(Random? rng = null) => _rng = rng ?? Random.Shared;
    public int RollDice() => _rng.Next(1, 4);
    public IReadOnlyList<int> ShuffleOrder(IReadOnlyList<int> npcIds)
        => npcIds.OrderBy(_ => _rng.Next()).ToList();
}
```

- [ ] **Step 2: Create test helper FixedRaceRandomizer**

```csharp
// tests/CubeRacing.Tests/Helpers/FixedRaceRandomizer.cs
using CubeRacing.Application.GameEngine;

namespace CubeRacing.Tests.Helpers;

public class FixedRaceRandomizer : IRaceRandomizer
{
    private readonly List<int> _fixedOrder;
    private readonly Queue<int> _diceQueue;

    public FixedRaceRandomizer(IEnumerable<int> order, IEnumerable<int> dice)
    {
        _fixedOrder = order.ToList();
        _diceQueue = new Queue<int>(dice);
    }

    public int RollDice() => _diceQueue.Dequeue();
    public IReadOnlyList<int> ShuffleOrder(IReadOnlyList<int> _) => _fixedOrder;
}
```

- [ ] **Step 3: Write failing tests**

```csharp
// tests/CubeRacing.Tests/GameEngine/RaceSimulatorTests.cs
using CubeRacing.Application.GameEngine;
using CubeRacing.Tests.Helpers;
using FluentAssertions;

namespace CubeRacing.Tests.GameEngine;

public class RaceSimulatorTests
{
    [Fact]
    public void AllNpcsStartAtSquareZero()
    {
        var sim = new RaceSimulator(4, 20);
        sim.GetSquareStacks()["0"].Should().HaveCount(4);
    }

    [Fact]
    public void NpcMovesForwardByDiceAmount()
    {
        var rand = new FixedRaceRandomizer(order: [1], dice: [2]);
        var sim = new RaceSimulator(1, 20, rand);

        var result = sim.SimulateRound();

        result.Actions.Should().HaveCount(1);
        result.Actions[0].NpcId.Should().Be(1);
        result.Actions[0].FromSquare.Should().Be(0);
        result.Actions[0].ToSquare.Should().Be(2);
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
        var rand = new FixedRaceRandomizer(order: [1, 2, 3], dice: [2, 1, 1]);
        var sim = RaceSimulator.CreateWithPositions(
            new Dictionary<int, List<int>> { [5] = [1, 2, 3] },
            mapLength: 20, rand);

        var result = sim.SimulateRound();

        var action = result.Actions[0];
        action.NpcId.Should().Be(1);
        action.FromSquare.Should().Be(5);
        action.ToSquare.Should().Be(7);
        action.CarriedNpcIds.Should().Equal([2, 3]);
        result.SquareStacks["7"].Should().Equal([1, 2, 3]);
    }

    [Fact]
    public void TopNpcMovesAloneWithoutCarryingNpcsBelow()
    {
        var rand = new FixedRaceRandomizer(order: [2, 1], dice: [3, 1]);
        var sim = RaceSimulator.CreateWithPositions(
            new Dictionary<int, List<int>> { [5] = [1, 2] },
            mapLength: 20, rand);

        var result = sim.SimulateRound();

        var action = result.Actions[0]; // NPC2 moves first
        action.NpcId.Should().Be(2);
        action.CarriedNpcIds.Should().BeEmpty();
        result.SquareStacks["5"].Should().Equal([1]);
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
        var rand = new FixedRaceRandomizer(order: [1], dice: [3]);
        var sim = RaceSimulator.CreateWithPositions(
            new Dictionary<int, List<int>> { [19] = [1] },
            mapLength: 20, rand);

        var result = sim.SimulateRound();

        result.Actions[0].ToSquare.Should().Be(20);
    }

    [Fact]
    public void GetWinnerReturnsNullWhenNobodyAtFinish()
    {
        var sim = new RaceSimulator(4, 20);
        sim.GetWinner().Should().BeNull();
    }
}
```

- [ ] **Step 4: Run tests — verify all fail**

```bash
dotnet test tests/CubeRacing.Tests --filter "FullyQualifiedName~RaceSimulatorTests"
```
Expected: compile error or 7 failures — `RaceSimulator` not yet defined.

- [ ] **Step 5: Implement RaceSimulator**

```csharp
// src/CubeRacing.Application/GameEngine/RaceSimulator.cs
namespace CubeRacing.Application.GameEngine;

public record RoundAction(int NpcId, int DiceRoll, int FromSquare, int ToSquare, List<int> CarriedNpcIds);
public record RoundResult(List<RoundAction> Actions, Dictionary<string, List<int>> SquareStacks, int? Winner);

public class RaceSimulator
{
    private readonly List<int>[] _squares;
    private readonly int _mapLength;
    private readonly IRaceRandomizer _randomizer;

    public RaceSimulator(int npcCount, int mapLength, IRaceRandomizer? randomizer = null)
    {
        _mapLength = mapLength;
        _randomizer = randomizer ?? new DefaultRaceRandomizer();
        _squares = InitSquares(mapLength + 1);
        for (int id = 1; id <= npcCount; id++)
            _squares[0].Add(id);
    }

    private RaceSimulator(List<int>[] squares, int mapLength, IRaceRandomizer randomizer)
    {
        _squares = squares;
        _mapLength = mapLength;
        _randomizer = randomizer;
    }

    public static RaceSimulator CreateWithPositions(
        Dictionary<int, List<int>> positions, int mapLength, IRaceRandomizer randomizer)
    {
        var squares = InitSquares(mapLength + 1);
        foreach (var (sq, stack) in positions)
            squares[sq].AddRange(stack);
        return new RaceSimulator(squares, mapLength, randomizer);
    }

    private static List<int>[] InitSquares(int size)
    {
        var arr = new List<int>[size];
        for (int i = 0; i < size; i++) arr[i] = new List<int>();
        return arr;
    }

    public RoundResult SimulateRound()
    {
        var allNpcs = _squares.SelectMany(s => s).ToList();
        var order = _randomizer.ShuffleOrder(allNpcs);
        var actions = new List<RoundAction>();

        foreach (int npcId in order)
        {
            var (fromSq, idx) = FindNpc(npcId);
            if (fromSq == _mapLength) continue; // already at finish

            int dice = _randomizer.RollDice();
            var moving = _squares[fromSq].Skip(idx).ToList();
            _squares[fromSq] = _squares[fromSq].Take(idx).ToList();

            int toSq = Math.Min(fromSq + dice, _mapLength);
            _squares[toSq].AddRange(moving);

            actions.Add(new RoundAction(npcId, dice, fromSq, toSq, moving.Skip(1).ToList()));
        }

        return new RoundResult(actions, GetSquareStacks(), GetWinner());
    }

    public int? GetWinner()
    {
        var finish = _squares[_mapLength];
        return finish.Count > 0 ? finish[^1] : null;
    }

    public Dictionary<string, List<int>> GetSquareStacks()
        => _squares
            .Select((stack, idx) => (idx, stack))
            .Where(x => x.stack.Count > 0)
            .ToDictionary(x => x.idx.ToString(), x => x.stack.ToList());

    private (int square, int index) FindNpc(int npcId)
    {
        for (int s = 0; s <= _mapLength; s++)
        {
            int i = _squares[s].IndexOf(npcId);
            if (i >= 0) return (s, i);
        }
        throw new InvalidOperationException($"NPC {npcId} not found in any square.");
    }
}
```

- [ ] **Step 6: Run tests — verify all pass**

```bash
dotnet test tests/CubeRacing.Tests --filter "FullyQualifiedName~RaceSimulatorTests"
```
Expected: `7 passed, 0 failed`.

- [ ] **Step 7: Commit**

```bash
git add src/CubeRacing.Application/GameEngine tests/CubeRacing.Tests
git commit -m "feat: implement RaceSimulator with stacking mechanic (TDD)"
```

---

### Task 4: SettlementCalculator (TDD)

**Files:**
- Create: `src/CubeRacing.Application/GameEngine/SettlementCalculator.cs`
- Create: `tests/CubeRacing.Tests/GameEngine/SettlementCalculatorTests.cs`

**Interfaces:**
- Consumes: `Bet` entity (from Domain)
- Produces:
  - `SettlementCalculator.Calculate(int winnerNpcId, IEnumerable<Bet> bets) → List<BetResult>`
  - `record BetResult(Guid BetId, int WinAmount)`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/CubeRacing.Tests/GameEngine/SettlementCalculatorTests.cs
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
        // totalPool=3, poolOnWinner=2, odds=1.5; bet=1 → floor(1.5)=1
        var betA = MakeBet(1, 1);
        var betB = MakeBet(2, 2);
        var calc = new SettlementCalculator();

        var results = calc.Calculate(winnerNpcId: 1, [betA, betB]);

        results.First(r => r.BetId == betA.Id).WinAmount.Should().Be(1);
    }
}
```

- [ ] **Step 2: Run tests — verify all fail**

```bash
dotnet test tests/CubeRacing.Tests --filter "FullyQualifiedName~SettlementCalculatorTests"
```
Expected: compile error — `SettlementCalculator` not yet defined.

- [ ] **Step 3: Implement SettlementCalculator**

```csharp
// src/CubeRacing.Application/GameEngine/SettlementCalculator.cs
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
```

- [ ] **Step 4: Run tests — verify all pass**

```bash
dotnet test tests/CubeRacing.Tests --filter "FullyQualifiedName~SettlementCalculatorTests"
```
Expected: `5 passed, 0 failed`.

- [ ] **Step 5: Commit**

```bash
git add src/CubeRacing.Application/GameEngine/SettlementCalculator.cs tests/CubeRacing.Tests/GameEngine/SettlementCalculatorTests.cs
git commit -m "feat: implement SettlementCalculator with pari-mutuel odds (TDD)"
```

---

### Task 5: Application Config, Interfaces & DTOs

**Files:**
- Create: `src/CubeRacing.Application/Config/GameSettings.cs`
- Create: `src/CubeRacing.Application/Config/NpcConfig.cs`
- Create: `src/CubeRacing.Application/Interfaces/IMessagePublisher.cs`
- Create: `src/CubeRacing.Application/Interfaces/IGameHubNotifier.cs`
- Create: `src/CubeRacing.Application/Interfaces/ICurrentSessionStore.cs`
- Create: `src/CubeRacing.Application/Interfaces/ISessionCompletionSignal.cs`
- Create: `src/CubeRacing.Application/Dtos/` (4 DTOs)

**Interfaces:**
- Produces: config/interface types used by all use cases and consumers

- [ ] **Step 1: Create config POCOs**

```csharp
// src/CubeRacing.Application/Config/GameSettings.cs
namespace CubeRacing.Application.Config;

public class GameSettings
{
    public int MapLength { get; init; } = 20;
    public int NpcCount { get; init; } = 4;
    public int BettingDurationSeconds { get; init; } = 60;
    public int WaitingDurationSeconds { get; init; } = 5;
    public int RoundIntervalMs { get; init; } = 1500;
    public int InitialChips { get; init; } = 1000;
}

// src/CubeRacing.Application/Config/NpcConfig.cs
namespace CubeRacing.Application.Config;

public class NpcConfig
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ColorHex { get; init; } = string.Empty;
}
```

- [ ] **Step 2: Create application interfaces**

```csharp
// src/CubeRacing.Application/Interfaces/IMessagePublisher.cs
namespace CubeRacing.Application.Interfaces;

public interface IMessagePublisher
{
    Task PublishAsync<T>(string queue, T message, CancellationToken ct = default);
}

// src/CubeRacing.Application/Interfaces/IGameHubNotifier.cs
using CubeRacing.Domain.Events;
namespace CubeRacing.Application.Interfaces;

public interface IGameHubNotifier
{
    Task NotifyOddsUpdatedAsync(Guid sessionId, object odds);
    Task NotifyBettingEndedAsync(Guid sessionId);
    Task NotifyRoundExecutedAsync(Guid sessionId, RoundExecutedEvent round);
    Task NotifyRaceCompletedAsync(Guid sessionId, int winnerNpcId);
    Task NotifySettlementDoneAsync(Guid sessionId, object result);
}

// src/CubeRacing.Application/Interfaces/ICurrentSessionStore.cs
namespace CubeRacing.Application.Interfaces;

public interface ICurrentSessionStore
{
    Guid? CurrentSessionId { get; }
    void Set(Guid sessionId);
}

// src/CubeRacing.Application/Interfaces/ISessionCompletionSignal.cs
namespace CubeRacing.Application.Interfaces;

public interface ISessionCompletionSignal
{
    Task WaitAsync(CancellationToken ct);
    void Signal();
}
```

- [ ] **Step 3: Create DTOs**

```csharp
// src/CubeRacing.Application/Dtos/NpcOddsDto.cs
namespace CubeRacing.Application.Dtos;
public record NpcOddsDto(int NpcId, double? Odds);

// src/CubeRacing.Application/Dtos/CurrentSessionDto.cs
namespace CubeRacing.Application.Dtos;
public record CurrentSessionDto(Guid SessionId, string Status, int? BettingSecondsRemaining, List<NpcOddsDto> NpcOdds, int MapLength);

// src/CubeRacing.Application/Dtos/LeaderboardEntryDto.cs
namespace CubeRacing.Application.Dtos;
public record LeaderboardEntryDto(string Nickname, int CorrectBets, int TotalChipsWon);

// src/CubeRacing.Application/Dtos/SettlementResultDto.cs
namespace CubeRacing.Application.Dtos;
public record SettlementResultDto(int WinnerNpcId, List<PlayerResultDto> PlayerResults, List<LeaderboardEntryDto> TopLeaderboard);
public record PlayerResultDto(Guid PlayerId, int WinAmount);
```

- [ ] **Step 4: Build and commit**

```bash
dotnet build
git add src/CubeRacing.Application
git commit -m "feat: add application config, interfaces, and DTOs"
```
Expected: `Build succeeded. 0 Error(s).`

---

### Task 6: EF Core, DbContext & Repositories

**Files:**
- Create: `src/CubeRacing.Infrastructure/Persistence/AppDbContext.cs`
- Create: `src/CubeRacing.Infrastructure/Persistence/Repositories/PlayerRepository.cs`
- Create: `src/CubeRacing.Infrastructure/Persistence/Repositories/GameSessionRepository.cs`
- Create: `src/CubeRacing.Infrastructure/Persistence/Repositories/BetRepository.cs`
- Create: `src/CubeRacing.Infrastructure/Persistence/Repositories/GameRoundRepository.cs`

**Interfaces:**
- Consumes: `IPlayerRepository`, `IGameSessionRepository`, `IBetRepository`, `IGameRoundRepository` (Domain)
- Produces: concrete EF Core implementations

- [ ] **Step 1: Create AppDbContext**

```csharp
// src/CubeRacing.Infrastructure/Persistence/AppDbContext.cs
using CubeRacing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CubeRacing.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Player> Players => Set<Player>();
    public DbSet<GameSession> GameSessions => Set<GameSession>();
    public DbSet<Bet> Bets => Set<Bet>();
    public DbSet<GameRound> GameRounds => Set<GameRound>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Player>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Nickname).HasMaxLength(50).IsRequired();
            e.HasIndex(p => p.Token).IsUnique();
        });

        mb.Entity<GameSession>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Status).HasConversion<string>();
        });

        mb.Entity<Bet>(e =>
        {
            e.HasKey(b => b.Id);
            e.HasIndex(b => new { b.PlayerId, b.SessionId }).IsUnique();
        });

        mb.Entity<GameRound>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.MovementDataJson).HasColumnType("nvarchar(max)");
        });
    }
}
```

- [ ] **Step 2: Create repositories**

```csharp
// src/CubeRacing.Infrastructure/Persistence/Repositories/PlayerRepository.cs
using CubeRacing.Domain.Entities;
using CubeRacing.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CubeRacing.Infrastructure.Persistence.Repositories;

public class PlayerRepository : IPlayerRepository
{
    private readonly AppDbContext _db;
    public PlayerRepository(AppDbContext db) => _db = db;

    public Task<Player?> GetByTokenAsync(Guid token, CancellationToken ct = default)
        => _db.Players.FirstOrDefaultAsync(p => p.Token == token, ct);

    public Task<Player?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Players.FindAsync([id], ct).AsTask();

    public async Task AddAsync(Player player, CancellationToken ct = default)
    {
        _db.Players.Add(player);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Player player, CancellationToken ct = default)
    {
        _db.Players.Update(player);
        await _db.SaveChangesAsync(ct);
    }

    public Task<List<Player>> GetTopByWinningsAsync(int count, CancellationToken ct = default)
        => _db.Players.OrderByDescending(p => p.TotalChipsWon).Take(count).ToListAsync(ct);
}

// src/CubeRacing.Infrastructure/Persistence/Repositories/GameSessionRepository.cs
using CubeRacing.Domain.Entities;
using CubeRacing.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CubeRacing.Infrastructure.Persistence.Repositories;

public class GameSessionRepository : IGameSessionRepository
{
    private readonly AppDbContext _db;
    public GameSessionRepository(AppDbContext db) => _db = db;

    public Task<GameSession?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.GameSessions.FindAsync([id], ct).AsTask();

    public async Task AddAsync(GameSession session, CancellationToken ct = default)
    {
        _db.GameSessions.Add(session);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(GameSession session, CancellationToken ct = default)
    {
        _db.GameSessions.Update(session);
        await _db.SaveChangesAsync(ct);
    }
}

// src/CubeRacing.Infrastructure/Persistence/Repositories/BetRepository.cs
using CubeRacing.Domain.Entities;
using CubeRacing.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CubeRacing.Infrastructure.Persistence.Repositories;

public class BetRepository : IBetRepository
{
    private readonly AppDbContext _db;
    public BetRepository(AppDbContext db) => _db = db;

    public Task<bool> ExistsAsync(Guid playerId, Guid sessionId, CancellationToken ct = default)
        => _db.Bets.AnyAsync(b => b.PlayerId == playerId && b.SessionId == sessionId, ct);

    public async Task AddAsync(Bet bet, CancellationToken ct = default)
    {
        _db.Bets.Add(bet);
        await _db.SaveChangesAsync(ct);
    }

    public Task<List<Bet>> GetBySessionAsync(Guid sessionId, CancellationToken ct = default)
        => _db.Bets.Where(b => b.SessionId == sessionId).ToListAsync(ct);

    public async Task UpdateAsync(Bet bet, CancellationToken ct = default)
    {
        _db.Bets.Update(bet);
        await _db.SaveChangesAsync(ct);
    }
}

// src/CubeRacing.Infrastructure/Persistence/Repositories/GameRoundRepository.cs
using CubeRacing.Domain.Entities;
using CubeRacing.Domain.Interfaces;

namespace CubeRacing.Infrastructure.Persistence.Repositories;

public class GameRoundRepository : IGameRoundRepository
{
    private readonly AppDbContext _db;
    public GameRoundRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(GameRound round, CancellationToken ct = default)
    {
        _db.GameRounds.Add(round);
        await _db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 3: Create and apply migration**

```bash
cd /Users/waffle/workspace/cube-racing/backend
dotnet ef migrations add InitialCreate --project src/CubeRacing.Infrastructure --startup-project src/CubeRacing.API
dotnet ef database update --project src/CubeRacing.Infrastructure --startup-project src/CubeRacing.API
```
Expected: Migration created; database tables created in SQL Server.

- [ ] **Step 4: Build and commit**

```bash
dotnet build
git add src/CubeRacing.Infrastructure/Persistence
git commit -m "feat: add EF Core context, repositories, and initial migration"
```

---

### Task 7: Application Use Cases

**Files:**
- Create: `src/CubeRacing.Application/UseCases/CreatePlayer.cs`
- Create: `src/CubeRacing.Application/UseCases/PlaceBet.cs`
- Create: `src/CubeRacing.Application/UseCases/GetCurrentSession.cs`
- Create: `src/CubeRacing.Application/UseCases/GetLeaderboard.cs`
- Create: `src/CubeRacing.Application/UseCases/SettleSession.cs`

**Interfaces:**
- Consumes: all repository interfaces, config, DTOs, `ICurrentSessionStore`, `IGameHubNotifier`
- Produces: use case classes used by controllers and consumers

- [ ] **Step 1: CreatePlayer use case**

```csharp
// src/CubeRacing.Application/UseCases/CreatePlayer.cs
using CubeRacing.Application.Config;
using CubeRacing.Domain.Entities;
using CubeRacing.Domain.Interfaces;
using Microsoft.Extensions.Options;

namespace CubeRacing.Application.UseCases;

public record CreatePlayerRequest(string Nickname);
public record CreatePlayerResponse(Guid PlayerId, Guid Token, int ChipsBalance);

public class CreatePlayer
{
    private readonly IPlayerRepository _players;
    private readonly GameSettings _settings;

    public CreatePlayer(IPlayerRepository players, IOptions<GameSettings> settings)
    {
        _players = players;
        _settings = settings.Value;
    }

    public async Task<CreatePlayerResponse> ExecuteAsync(CreatePlayerRequest req, CancellationToken ct = default)
    {
        var player = Player.Create(req.Nickname.Trim(), _settings.InitialChips);
        await _players.AddAsync(player, ct);
        return new CreatePlayerResponse(player.Id, player.Token, player.ChipsBalance);
    }
}
```

- [ ] **Step 2: PlaceBet use case**

```csharp
// src/CubeRacing.Application/UseCases/PlaceBet.cs
using CubeRacing.Application.Interfaces;
using CubeRacing.Domain.Entities;
using CubeRacing.Domain.Enums;
using CubeRacing.Domain.Interfaces;

namespace CubeRacing.Application.UseCases;

public record PlaceBetRequest(Guid PlayerId, Guid SessionId, int NpcId, int Amount);

public enum PlaceBetError { SessionNotFound, BettingClosed, AlreadyBet, InsufficientChips, InvalidNpcId }
public record PlaceBetResult(bool Success, PlaceBetError? Error = null);

public class PlaceBet
{
    private readonly IGameSessionRepository _sessions;
    private readonly IBetRepository _bets;
    private readonly IPlayerRepository _players;
    private readonly IGameHubNotifier _hub;
    private readonly IBetRepository _betRepo;

    public PlaceBet(IGameSessionRepository sessions, IBetRepository bets,
        IPlayerRepository players, IGameHubNotifier hub)
    {
        _sessions = sessions;
        _bets = bets;
        _players = players;
        _hub = hub;
        _betRepo = bets;
    }

    public async Task<PlaceBetResult> ExecuteAsync(PlaceBetRequest req, CancellationToken ct = default)
    {
        if (req.NpcId < 1 || req.NpcId > 4)
            return new PlaceBetResult(false, PlaceBetError.InvalidNpcId);

        var session = await _sessions.GetByIdAsync(req.SessionId, ct);
        if (session is null)
            return new PlaceBetResult(false, PlaceBetError.SessionNotFound);

        if (session.Status != GameStatus.Betting || DateTime.UtcNow > session.BettingDeadline)
            return new PlaceBetResult(false, PlaceBetError.BettingClosed);

        if (await _bets.ExistsAsync(req.PlayerId, req.SessionId, ct))
            return new PlaceBetResult(false, PlaceBetError.AlreadyBet);

        var player = await _players.GetByIdAsync(req.PlayerId, ct);
        if (player is null || player.ChipsBalance < req.Amount)
            return new PlaceBetResult(false, PlaceBetError.InsufficientChips);

        player.DeductChips(req.Amount);
        await _players.UpdateAsync(player, ct);

        var bet = Bet.Create(req.PlayerId, req.SessionId, req.NpcId, req.Amount);
        await _bets.AddAsync(bet, ct);

        session.AddToPool(req.Amount);
        await _sessions.UpdateAsync(session, ct);

        // Broadcast updated odds
        var allBets = await _betRepo.GetBySessionAsync(req.SessionId, ct);
        var odds = CalculateOdds(allBets, session.TotalPool);
        await _hub.NotifyOddsUpdatedAsync(req.SessionId, odds);

        return new PlaceBetResult(true);
    }

    private static object CalculateOdds(List<Bet> bets, int totalPool)
    {
        var poolByNpc = bets.GroupBy(b => b.NpcId).ToDictionary(g => g.Key, g => g.Sum(b => b.Amount));
        return Enumerable.Range(1, 4).Select(id =>
        {
            double? odds = poolByNpc.TryGetValue(id, out var pool) && pool > 0
                ? (double)totalPool / pool
                : null;
            return new { npcId = id, odds };
        }).ToList();
    }
}
```

- [ ] **Step 3: GetCurrentSession use case**

```csharp
// src/CubeRacing.Application/UseCases/GetCurrentSession.cs
using CubeRacing.Application.Dtos;
using CubeRacing.Application.Interfaces;
using CubeRacing.Domain.Enums;
using CubeRacing.Domain.Interfaces;

namespace CubeRacing.Application.UseCases;

public class GetCurrentSession
{
    private readonly ICurrentSessionStore _store;
    private readonly IGameSessionRepository _sessions;
    private readonly IBetRepository _bets;

    public GetCurrentSession(ICurrentSessionStore store, IGameSessionRepository sessions, IBetRepository bets)
    {
        _store = store;
        _sessions = sessions;
        _bets = bets;
    }

    public async Task<CurrentSessionDto?> ExecuteAsync(CancellationToken ct = default)
    {
        if (_store.CurrentSessionId is null) return null;

        var session = await _sessions.GetByIdAsync(_store.CurrentSessionId.Value, ct);
        if (session is null) return null;

        var allBets = await _bets.GetBySessionAsync(session.Id, ct);
        var poolByNpc = allBets.GroupBy(b => b.NpcId).ToDictionary(g => g.Key, g => g.Sum(b => b.Amount));
        var npcOdds = Enumerable.Range(1, 4).Select(id =>
        {
            double? odds = poolByNpc.TryGetValue(id, out var pool) && pool > 0 && session.TotalPool > 0
                ? (double)session.TotalPool / pool
                : null;
            return new NpcOddsDto(id, odds);
        }).ToList();

        int? remaining = session.Status == GameStatus.Betting
            ? Math.Max(0, (int)(session.BettingDeadline - DateTime.UtcNow).TotalSeconds)
            : null;

        return new CurrentSessionDto(session.Id, session.Status.ToString(), remaining, npcOdds, session.MapLength);
    }
}
```

- [ ] **Step 4: GetLeaderboard and SettleSession use cases**

```csharp
// src/CubeRacing.Application/UseCases/GetLeaderboard.cs
using CubeRacing.Application.Dtos;
using CubeRacing.Domain.Interfaces;

namespace CubeRacing.Application.UseCases;

public class GetLeaderboard
{
    private readonly IPlayerRepository _players;
    public GetLeaderboard(IPlayerRepository players) => _players = players;

    public async Task<List<LeaderboardEntryDto>> ExecuteAsync(CancellationToken ct = default)
    {
        var top = await _players.GetTopByWinningsAsync(20, ct);
        return top.Select(p => new LeaderboardEntryDto(p.Nickname, p.CorrectBets, p.TotalChipsWon)).ToList();
    }
}

// src/CubeRacing.Application/UseCases/SettleSession.cs
using CubeRacing.Application.Dtos;
using CubeRacing.Application.GameEngine;
using CubeRacing.Domain.Interfaces;

namespace CubeRacing.Application.UseCases;

public class SettleSession
{
    private readonly IGameSessionRepository _sessions;
    private readonly IBetRepository _bets;
    private readonly IPlayerRepository _players;
    private readonly SettlementCalculator _calculator;

    public SettleSession(IGameSessionRepository sessions, IBetRepository bets,
        IPlayerRepository players, SettlementCalculator calculator)
    {
        _sessions = sessions;
        _bets = bets;
        _players = players;
        _calculator = calculator;
    }

    public async Task<SettlementResultDto> ExecuteAsync(Guid sessionId, int winnerNpcId, CancellationToken ct = default)
    {
        var session = await _sessions.GetByIdAsync(sessionId, ct)
            ?? throw new InvalidOperationException($"Session {sessionId} not found.");

        var bets = await _bets.GetBySessionAsync(sessionId, ct);
        var results = _calculator.Calculate(winnerNpcId, bets);

        var playerResults = new List<PlayerResultDto>();
        foreach (var result in results)
        {
            var bet = bets.First(b => b.Id == result.BetId);
            bet.SetWinAmount(result.WinAmount);
            await _bets.UpdateAsync(bet, ct);

            if (result.WinAmount > 0)
            {
                var player = await _players.GetByIdAsync(bet.PlayerId, ct);
                if (player is not null)
                {
                    player.AddWinnings(result.WinAmount);
                    await _players.UpdateAsync(player, ct);
                }
            }
            playerResults.Add(new PlayerResultDto(bet.PlayerId, result.WinAmount));
        }

        session.Complete(winnerNpcId);
        await _sessions.UpdateAsync(session, ct);

        var top = await _players.GetTopByWinningsAsync(5, ct);
        var leaderboard = top.Select(p => new LeaderboardEntryDto(p.Nickname, p.CorrectBets, p.TotalChipsWon)).ToList();
        return new SettlementResultDto(winnerNpcId, playerResults, leaderboard);
    }
}
```

- [ ] **Step 5: Build and commit**

```bash
dotnet build
git add src/CubeRacing.Application/UseCases
git commit -m "feat: add application use cases (CreatePlayer, PlaceBet, GetCurrentSession, GetLeaderboard, SettleSession)"
```
Expected: `Build succeeded. 0 Error(s).`

---

### Task 8: RabbitMQ Infrastructure & Consumers

**Files:**
- Create: `src/CubeRacing.Infrastructure/Messaging/RabbitMqPublisher.cs`
- Create: `src/CubeRacing.Infrastructure/Messaging/RabbitMqConsumerBase.cs`
- Create: `src/CubeRacing.Infrastructure/Messaging/Consumers/BettingEndedConsumer.cs`
- Create: `src/CubeRacing.Infrastructure/Messaging/Consumers/RoundExecutedConsumer.cs`
- Create: `src/CubeRacing.Infrastructure/Messaging/Consumers/RaceCompletedConsumer.cs`
- Create: `src/CubeRacing.Infrastructure/Messaging/Consumers/SettlementDoneConsumer.cs`

**Interfaces:**
- Consumes: `IMessagePublisher`, `IGameHubNotifier`, use cases, domain events
- Produces: `RabbitMqPublisher` implementing `IMessagePublisher`; 4 consumers as `BackgroundService`

- [ ] **Step 1: Create publisher**

```csharp
// src/CubeRacing.Infrastructure/Messaging/RabbitMqPublisher.cs
using System.Text;
using System.Text.Json;
using CubeRacing.Application.Interfaces;
using RabbitMQ.Client;

namespace CubeRacing.Infrastructure.Messaging;

public class RabbitMqPublisher : IMessagePublisher, IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;

    public RabbitMqPublisher(IConnectionFactory factory)
    {
        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
    }

    public Task PublishAsync<T>(string queue, T message, CancellationToken ct = default)
    {
        _channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false);
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        var props = _channel.CreateBasicProperties();
        props.Persistent = true;
        _channel.BasicPublish("", queue, props, body);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _channel.Dispose();
        _connection.Dispose();
    }
}
```

- [ ] **Step 2: Create consumer base class**

```csharp
// src/CubeRacing.Infrastructure/Messaging/RabbitMqConsumerBase.cs
using System.Text;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace CubeRacing.Infrastructure.Messaging;

public abstract class RabbitMqConsumerBase : BackgroundService
{
    private readonly IConnectionFactory _factory;
    private readonly string _queue;

    protected RabbitMqConsumerBase(IConnectionFactory factory, string queue)
    {
        _factory = factory;
        _queue = queue;
    }

    protected override Task ExecuteAsync(CancellationToken ct)
    {
        var connection = _factory.CreateConnection();
        var channel = connection.CreateModel();
        channel.QueueDeclare(_queue, durable: true, exclusive: false, autoDelete: false);
        channel.BasicQos(0, 1, false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (_, ea) =>
        {
            try
            {
                var body = Encoding.UTF8.GetString(ea.Body.ToArray());
                await HandleAsync(body, ct);
                channel.BasicAck(ea.DeliveryTag, false);
            }
            catch
            {
                channel.BasicNack(ea.DeliveryTag, false, requeue: false);
            }
        };
        channel.BasicConsume(_queue, autoAck: false, consumer);

        ct.WaitHandle.WaitOne();
        channel.Dispose();
        connection.Dispose();
        return Task.CompletedTask;
    }

    protected abstract Task HandleAsync(string messageJson, CancellationToken ct);
}
```

Note: To enable `AsyncEventingBasicConsumer`, the `ConnectionFactory` must have `DispatchConsumersAsync = true` (set in `Program.cs`).

- [ ] **Step 3: BettingEndedConsumer — race loop**

```csharp
// src/CubeRacing.Infrastructure/Messaging/Consumers/BettingEndedConsumer.cs
using System.Text.Json;
using CubeRacing.Application.Config;
using CubeRacing.Application.GameEngine;
using CubeRacing.Application.Interfaces;
using CubeRacing.Domain.Entities;
using CubeRacing.Domain.Events;
using CubeRacing.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace CubeRacing.Infrastructure.Messaging.Consumers;

public class BettingEndedConsumer : RabbitMqConsumerBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMessagePublisher _publisher;
    private readonly GameSettings _settings;

    public BettingEndedConsumer(IConnectionFactory factory, IServiceScopeFactory scopeFactory,
        IMessagePublisher publisher, IOptions<GameSettings> settings)
        : base(factory, "betting.ended")
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _settings = settings.Value;
    }

    protected override async Task HandleAsync(string json, CancellationToken ct)
    {
        var ev = JsonSerializer.Deserialize<BettingEndedEvent>(json)!;

        using var scope = _scopeFactory.CreateScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IGameSessionRepository>();
        var roundRepo = scope.ServiceProvider.GetRequiredService<IGameRoundRepository>();
        var hubNotifier = scope.ServiceProvider.GetRequiredService<IGameHubNotifier>();

        var session = await sessionRepo.GetByIdAsync(ev.SessionId, ct);
        if (session is null) return;

        session.StartRacing();
        await sessionRepo.UpdateAsync(session, ct);
        await hubNotifier.NotifyBettingEndedAsync(ev.SessionId);

        var simulator = new RaceSimulator(_settings.NpcCount, _settings.MapLength);
        int roundNumber = 0;

        while (simulator.GetWinner() is null)
        {
            roundNumber++;
            var result = simulator.SimulateRound();

            var round = GameRound.Create(ev.SessionId, roundNumber, JsonSerializer.Serialize(result));
            await roundRepo.AddAsync(round, ct);

            var roundEvent = new RoundExecutedEvent(
                ev.SessionId, roundNumber,
                result.Actions.Select(a => new RoundActionDto(a.NpcId, a.DiceRoll, a.FromSquare, a.ToSquare, a.CarriedNpcIds)).ToList(),
                result.SquareStacks,
                result.Winner);

            await _publisher.PublishAsync("round.executed", roundEvent, ct);

            if (result.Winner is null)
                await Task.Delay(_settings.RoundIntervalMs, ct);
        }

        await _publisher.PublishAsync("race.completed",
            new RaceCompletedEvent(ev.SessionId, simulator.GetWinner()!.Value), ct);
    }
}
```

- [ ] **Step 4: RoundExecutedConsumer — SignalR broadcast**

```csharp
// src/CubeRacing.Infrastructure/Messaging/Consumers/RoundExecutedConsumer.cs
using System.Text.Json;
using CubeRacing.Application.Interfaces;
using CubeRacing.Domain.Events;
using RabbitMQ.Client;

namespace CubeRacing.Infrastructure.Messaging.Consumers;

public class RoundExecutedConsumer : RabbitMqConsumerBase
{
    private readonly IGameHubNotifier _hub;

    public RoundExecutedConsumer(IConnectionFactory factory, IGameHubNotifier hub)
        : base(factory, "round.executed")
        => _hub = hub;

    protected override async Task HandleAsync(string json, CancellationToken ct)
    {
        var ev = JsonSerializer.Deserialize<RoundExecutedEvent>(json)!;
        await _hub.NotifyRoundExecutedAsync(ev.SessionId, ev);
    }
}
```

- [ ] **Step 5: RaceCompletedConsumer — settlement**

```csharp
// src/CubeRacing.Infrastructure/Messaging/Consumers/RaceCompletedConsumer.cs
using System.Text.Json;
using CubeRacing.Application.Interfaces;
using CubeRacing.Application.UseCases;
using CubeRacing.Domain.Events;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace CubeRacing.Infrastructure.Messaging.Consumers;

public class RaceCompletedConsumer : RabbitMqConsumerBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMessagePublisher _publisher;
    private readonly IGameHubNotifier _hub;

    public RaceCompletedConsumer(IConnectionFactory factory, IServiceScopeFactory scopeFactory,
        IMessagePublisher publisher, IGameHubNotifier hub)
        : base(factory, "race.completed")
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _hub = hub;
    }

    protected override async Task HandleAsync(string json, CancellationToken ct)
    {
        var ev = JsonSerializer.Deserialize<RaceCompletedEvent>(json)!;

        using var scope = _scopeFactory.CreateScope();
        var settle = scope.ServiceProvider.GetRequiredService<SettleSession>();
        var result = await settle.ExecuteAsync(ev.SessionId, ev.WinnerNpcId, ct);

        await _hub.NotifyRaceCompletedAsync(ev.SessionId, ev.WinnerNpcId);
        await _hub.NotifySettlementDoneAsync(ev.SessionId, result);
        await _publisher.PublishAsync("settlement.done", new SettlementDoneEvent(ev.SessionId), ct);
    }
}
```

- [ ] **Step 6: SettlementDoneConsumer — trigger next session**

```csharp
// src/CubeRacing.Infrastructure/Messaging/Consumers/SettlementDoneConsumer.cs
using System.Text.Json;
using CubeRacing.Application.Interfaces;
using CubeRacing.Domain.Events;
using RabbitMQ.Client;

namespace CubeRacing.Infrastructure.Messaging.Consumers;

public class SettlementDoneConsumer : RabbitMqConsumerBase
{
    private readonly ISessionCompletionSignal _signal;

    public SettlementDoneConsumer(IConnectionFactory factory, ISessionCompletionSignal signal)
        : base(factory, "settlement.done")
        => _signal = signal;

    protected override Task HandleAsync(string json, CancellationToken ct)
    {
        _signal.Signal();
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 7: Build and commit**

```bash
dotnet build
git add src/CubeRacing.Infrastructure/Messaging
git commit -m "feat: add RabbitMQ publisher, consumer base, and 4 phase consumers"
```

---

### Task 9: SignalR Hub, GameHubNotifier & Session Services

**Files:**
- Create: `src/CubeRacing.Infrastructure/Hubs/GameHub.cs`
- Create: `src/CubeRacing.Infrastructure/Services/GameHubNotifier.cs`
- Create: `src/CubeRacing.Infrastructure/Services/CurrentSessionStore.cs`
- Create: `src/CubeRacing.Infrastructure/Services/SessionCompletionSignal.cs`
- Create: `src/CubeRacing.Infrastructure/Services/GameSessionManager.cs`

**Interfaces:**
- Produces: `IGameHubNotifier` implementation; `ICurrentSessionStore` impl; `ISessionCompletionSignal` impl; `GameSessionManager` BackgroundService

- [ ] **Step 1: Create GameHub**

```csharp
// src/CubeRacing.Infrastructure/Hubs/GameHub.cs
using Microsoft.AspNetCore.SignalR;

namespace CubeRacing.Infrastructure.Hubs;

public class GameHub : Hub
{
    public async Task JoinSession(string sessionId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);

    public async Task LeaveSession(string sessionId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
}
```

- [ ] **Step 2: Create GameHubNotifier**

```csharp
// src/CubeRacing.Infrastructure/Services/GameHubNotifier.cs
using CubeRacing.Application.Interfaces;
using CubeRacing.Domain.Events;
using CubeRacing.Infrastructure.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CubeRacing.Infrastructure.Services;

public class GameHubNotifier : IGameHubNotifier
{
    private readonly IHubContext<GameHub> _hub;
    public GameHubNotifier(IHubContext<GameHub> hub) => _hub = hub;

    public Task NotifyOddsUpdatedAsync(Guid sessionId, object odds)
        => _hub.Clients.Group(sessionId.ToString()).SendAsync("OddsUpdated", odds);

    public Task NotifyBettingEndedAsync(Guid sessionId)
        => _hub.Clients.Group(sessionId.ToString()).SendAsync("BettingEnded");

    public Task NotifyRoundExecutedAsync(Guid sessionId, RoundExecutedEvent round)
        => _hub.Clients.Group(sessionId.ToString()).SendAsync("RoundExecuted", round);

    public Task NotifyRaceCompletedAsync(Guid sessionId, int winnerNpcId)
        => _hub.Clients.Group(sessionId.ToString()).SendAsync("RaceCompleted", new { winnerNpcId });

    public Task NotifySettlementDoneAsync(Guid sessionId, object result)
        => _hub.Clients.Group(sessionId.ToString()).SendAsync("SettlementDone", result);
}
```

- [ ] **Step 3: Create CurrentSessionStore and SessionCompletionSignal**

```csharp
// src/CubeRacing.Infrastructure/Services/CurrentSessionStore.cs
using CubeRacing.Application.Interfaces;

namespace CubeRacing.Infrastructure.Services;

public class CurrentSessionStore : ICurrentSessionStore
{
    private Guid? _id;
    public Guid? CurrentSessionId => _id;
    public void Set(Guid sessionId) => _id = sessionId;
}

// src/CubeRacing.Infrastructure/Services/SessionCompletionSignal.cs
using CubeRacing.Application.Interfaces;

namespace CubeRacing.Infrastructure.Services;

public class SessionCompletionSignal : ISessionCompletionSignal
{
    private TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitAsync(CancellationToken ct) => _tcs.Task.WaitAsync(ct);

    public void Signal()
    {
        var old = Interlocked.Exchange(ref _tcs, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        old.TrySetResult();
    }
}
```

- [ ] **Step 4: Create GameSessionManager**

```csharp
// src/CubeRacing.Infrastructure/Services/GameSessionManager.cs
using CubeRacing.Application.Config;
using CubeRacing.Application.Interfaces;
using CubeRacing.Domain.Entities;
using CubeRacing.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CubeRacing.Infrastructure.Services;

public class GameSessionManager : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICurrentSessionStore _store;
    private readonly IMessagePublisher _publisher;
    private readonly ISessionCompletionSignal _signal;
    private readonly GameSettings _settings;

    public GameSessionManager(IServiceScopeFactory scopeFactory, ICurrentSessionStore store,
        IMessagePublisher publisher, ISessionCompletionSignal signal, IOptions<GameSettings> settings)
    {
        _scopeFactory = scopeFactory;
        _store = store;
        _publisher = publisher;
        _signal = signal;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await RunSessionLifecycleAsync(ct);
        }
    }

    private async Task RunSessionLifecycleAsync(CancellationToken ct)
    {
        // Create session
        using var scope = _scopeFactory.CreateScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IGameSessionRepository>();

        var session = GameSession.CreateNew(_settings.MapLength);
        await sessionRepo.AddAsync(session, ct);
        _store.Set(session.Id);

        // Waiting phase
        await Task.Delay(TimeSpan.FromSeconds(_settings.WaitingDurationSeconds), ct);

        // Betting phase
        session.StartBetting(_settings.BettingDurationSeconds);
        await sessionRepo.UpdateAsync(session, ct);

        var remaining = session.BettingDeadline - DateTime.UtcNow;
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, ct);

        // Trigger race
        await _publisher.PublishAsync("betting.ended",
            new CubeRacing.Domain.Events.BettingEndedEvent(session.Id), ct);

        // Wait for settlement to complete (SettlementDoneConsumer signals this)
        await _signal.WaitAsync(ct);
    }
}
```

- [ ] **Step 5: Build and commit**

```bash
dotnet build
git add src/CubeRacing.Infrastructure/Hubs src/CubeRacing.Infrastructure/Services
git commit -m "feat: add SignalR hub, notifier, session store, and game session manager"
```

---

### Task 10: API Controllers & Token Middleware

**Files:**
- Create: `src/CubeRacing.API/Middleware/TokenAuthMiddleware.cs`
- Create: `src/CubeRacing.API/Controllers/PlayersController.cs`
- Create: `src/CubeRacing.API/Controllers/SessionsController.cs`
- Create: `src/CubeRacing.API/Controllers/LeaderboardController.cs`

**Interfaces:**
- Consumes: all use cases, `ICurrentSessionStore`
- Produces: REST endpoints for Unity client

- [ ] **Step 1: Token middleware**

```csharp
// src/CubeRacing.API/Middleware/TokenAuthMiddleware.cs
using CubeRacing.Domain.Interfaces;

namespace CubeRacing.API.Middleware;

public class TokenAuthMiddleware
{
    private readonly RequestDelegate _next;
    public TokenAuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, IPlayerRepository players)
    {
        var header = ctx.Request.Headers["Authorization"].FirstOrDefault();
        if (header?.StartsWith("Bearer ") == true &&
            Guid.TryParse(header["Bearer ".Length..], out var token))
        {
            var player = await players.GetByTokenAsync(token, ctx.RequestAborted);
            if (player is not null) ctx.Items["Player"] = player;
        }
        await _next(ctx);
    }
}
```

- [ ] **Step 2: PlayersController**

```csharp
// src/CubeRacing.API/Controllers/PlayersController.cs
using CubeRacing.Application.UseCases;
using Microsoft.AspNetCore.Mvc;

namespace CubeRacing.API.Controllers;

[ApiController]
[Route("api/players")]
public class PlayersController : ControllerBase
{
    private readonly CreatePlayer _createPlayer;
    public PlayersController(CreatePlayer createPlayer) => _createPlayer = createPlayer;

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePlayerRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Nickname))
            return BadRequest(new { error = "Nickname is required." });

        var result = await _createPlayer.ExecuteAsync(req, ct);
        return Ok(result);
    }
}
```

- [ ] **Step 3: SessionsController**

```csharp
// src/CubeRacing.API/Controllers/SessionsController.cs
using CubeRacing.Application.Interfaces;
using CubeRacing.Application.UseCases;
using CubeRacing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace CubeRacing.API.Controllers;

[ApiController]
[Route("api/sessions")]
public class SessionsController : ControllerBase
{
    private readonly GetCurrentSession _getCurrent;
    private readonly PlaceBet _placeBet;
    private readonly ICurrentSessionStore _store;

    public SessionsController(GetCurrentSession getCurrent, PlaceBet placeBet, ICurrentSessionStore store)
    {
        _getCurrent = getCurrent;
        _placeBet = placeBet;
        _store = store;
    }

    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(CancellationToken ct)
    {
        var session = await _getCurrent.ExecuteAsync(ct);
        return session is null ? NotFound() : Ok(session);
    }

    [HttpPost("{sessionId:guid}/bets")]
    public async Task<IActionResult> PlaceBet(Guid sessionId, [FromBody] PlaceBetBody body, CancellationToken ct)
    {
        var player = HttpContext.Items["Player"] as Player;
        if (player is null) return Unauthorized();

        var req = new PlaceBetRequest(player.Id, sessionId, body.NpcId, body.Amount);
        var result = await _placeBet.ExecuteAsync(req, ct);

        if (!result.Success)
        {
            return result.Error switch
            {
                PlaceBetError.BettingClosed => Conflict(new { error = "Betting is closed." }),
                PlaceBetError.AlreadyBet => Conflict(new { error = "You have already placed a bet this session." }),
                PlaceBetError.InsufficientChips => BadRequest(new { error = "Insufficient chips." }),
                PlaceBetError.InvalidNpcId => BadRequest(new { error = "Invalid NPC ID. Must be 1–4." }),
                _ => NotFound(new { error = "Session not found." })
            };
        }

        return Ok(new { success = true });
    }
}

public record PlaceBetBody(int NpcId, int Amount);
```

- [ ] **Step 4: LeaderboardController**

```csharp
// src/CubeRacing.API/Controllers/LeaderboardController.cs
using CubeRacing.Application.UseCases;
using Microsoft.AspNetCore.Mvc;

namespace CubeRacing.API.Controllers;

[ApiController]
[Route("api/leaderboard")]
public class LeaderboardController : ControllerBase
{
    private readonly GetLeaderboard _getLeaderboard;
    public LeaderboardController(GetLeaderboard getLeaderboard) => _getLeaderboard = getLeaderboard;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(await _getLeaderboard.ExecuteAsync(ct));
}
```

- [ ] **Step 5: Build and commit**

```bash
dotnet build
git add src/CubeRacing.API
git commit -m "feat: add REST controllers and token auth middleware"
```

---

### Task 11: Program.cs — DI & App Configuration

**Files:**
- Modify: `src/CubeRacing.API/Program.cs`

**Interfaces:**
- Consumes: all infrastructure and application types
- Produces: runnable API

- [ ] **Step 1: Write Program.cs**

```csharp
// src/CubeRacing.API/Program.cs
using CubeRacing.Application.Config;
using CubeRacing.Application.GameEngine;
using CubeRacing.Application.Interfaces;
using CubeRacing.Application.UseCases;
using CubeRacing.Domain.Interfaces;
using CubeRacing.Infrastructure.Hubs;
using CubeRacing.Infrastructure.Messaging;
using CubeRacing.Infrastructure.Messaging.Consumers;
using CubeRacing.Infrastructure.Persistence;
using CubeRacing.Infrastructure.Persistence.Repositories;
using CubeRacing.Infrastructure.Services;
using CubeRacing.API.Middleware;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

// Config
builder.Services.Configure<GameSettings>(builder.Configuration.GetSection("GameSettings"));
builder.Services.Configure<List<NpcConfig>>(builder.Configuration.GetSection("Npcs"));

// EF Core
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Repositories
builder.Services.AddScoped<IPlayerRepository, PlayerRepository>();
builder.Services.AddScoped<IGameSessionRepository, GameSessionRepository>();
builder.Services.AddScoped<IBetRepository, BetRepository>();
builder.Services.AddScoped<IGameRoundRepository, GameRoundRepository>();

// RabbitMQ
builder.Services.AddSingleton<IConnectionFactory>(_ =>
    new ConnectionFactory
    {
        HostName = builder.Configuration["RabbitMQ:Host"] ?? "localhost",
        Port = int.Parse(builder.Configuration["RabbitMQ:Port"] ?? "5672"),
        UserName = builder.Configuration["RabbitMQ:Username"] ?? "guest",
        Password = builder.Configuration["RabbitMQ:Password"] ?? "guest",
        DispatchConsumersAsync = true
    });
builder.Services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();

// SignalR
builder.Services.AddSignalR();
builder.Services.AddSingleton<IGameHubNotifier, GameHubNotifier>();

// Application singletons
builder.Services.AddSingleton<ICurrentSessionStore, CurrentSessionStore>();
builder.Services.AddSingleton<ISessionCompletionSignal, SessionCompletionSignal>();

// Application use cases (scoped)
builder.Services.AddScoped<CreatePlayer>();
builder.Services.AddScoped<PlaceBet>();
builder.Services.AddScoped<GetCurrentSession>();
builder.Services.AddScoped<GetLeaderboard>();
builder.Services.AddScoped<SettleSession>();
builder.Services.AddScoped<SettlementCalculator>();

// Background services
builder.Services.AddHostedService<GameSessionManager>();
builder.Services.AddHostedService<BettingEndedConsumer>();
builder.Services.AddHostedService<RoundExecutedConsumer>();
builder.Services.AddHostedService<RaceCompletedConsumer>();
builder.Services.AddHostedService<SettlementDoneConsumer>();

// Web API
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

// Auto-migrate on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseMiddleware<TokenAuthMiddleware>();
app.MapControllers();
app.MapHub<GameHub>("/hubs/game");

app.Run();
```

- [ ] **Step 2: Run the API and smoke test**

```bash
dotnet run --project src/CubeRacing.API
```
Expected: API starts. Visit `http://localhost:5000/swagger` — all endpoints listed.

Test `POST /api/players`:
```bash
curl -X POST http://localhost:5000/api/players \
  -H "Content-Type: application/json" \
  -d '{"nickname": "TestPlayer"}'
```
Expected: `{ "playerId": "...", "token": "...", "chipsBalance": 1000 }`

- [ ] **Step 3: Commit**

```bash
git add src/CubeRacing.API/Program.cs
git commit -m "feat: wire up DI, middleware, SignalR, and background services in Program.cs"
```

---

### Task 12: Integration Tests — PlaceBet Edge Cases

**Files:**
- Create: `tests/CubeRacing.Tests/Helpers/TestDbContextFactory.cs`
- Create: `tests/CubeRacing.Tests/UseCases/PlaceBetTests.cs`

**Interfaces:**
- Consumes: `PlaceBet`, `AppDbContext` (in-memory), all repositories
- Produces: verified edge-case coverage for the most critical use case

- [ ] **Step 1: Create test DB factory**

```csharp
// tests/CubeRacing.Tests/Helpers/TestDbContextFactory.cs
using CubeRacing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubeRacing.Tests.Helpers;

public static class TestDbContextFactory
{
    public static AppDbContext Create()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(opts);
    }
}
```

- [ ] **Step 2: Write failing integration tests**

```csharp
// tests/CubeRacing.Tests/UseCases/PlaceBetTests.cs
using CubeRacing.Application.UseCases;
using CubeRacing.Domain.Entities;
using CubeRacing.Domain.Enums;
using CubeRacing.Infrastructure.Persistence.Repositories;
using CubeRacing.Tests.Helpers;
using FluentAssertions;
using Moq;

namespace CubeRacing.Tests.UseCases;

public class PlaceBetTests
{
    private static (PlaceBet useCase, AppDbContext db) BuildSut()
    {
        var db = TestDbContextFactory.Create();
        var playerRepo = new PlayerRepository(db);
        var sessionRepo = new GameSessionRepository(db);
        var betRepo = new BetRepository(db);
        var hubMock = new Mock<CubeRacing.Application.Interfaces.IGameHubNotifier>();
        hubMock.Setup(h => h.NotifyOddsUpdatedAsync(It.IsAny<Guid>(), It.IsAny<object>()))
               .Returns(Task.CompletedTask);
        var useCase = new PlaceBet(sessionRepo, betRepo, playerRepo, hubMock.Object);
        return (useCase, db);
    }

    private static async Task<(Player player, GameSession session)> SeedAsync(AppDbContext db)
    {
        var player = Player.Create("Tester", 1000);
        db.Players.Add(player);

        var session = GameSession.CreateNew();
        session.StartBetting(60);
        db.GameSessions.Add(session);

        await db.SaveChangesAsync();
        return (player, session);
    }

    [Fact]
    public async Task SuccessfulBet_DeductsChipsAndCreatesRecord()
    {
        var (sut, db) = BuildSut();
        var (player, session) = await SeedAsync(db);

        var result = await sut.ExecuteAsync(new PlaceBetRequest(player.Id, session.Id, 1, 200));

        result.Success.Should().BeTrue();
        db.Bets.Should().HaveCount(1);
        db.Players.Find(player.Id)!.ChipsBalance.Should().Be(800);
    }

    [Fact]
    public async Task DuplicateBet_ReturnAlreadyBetError()
    {
        var (sut, db) = BuildSut();
        var (player, session) = await SeedAsync(db);

        await sut.ExecuteAsync(new PlaceBetRequest(player.Id, session.Id, 1, 100));
        var result = await sut.ExecuteAsync(new PlaceBetRequest(player.Id, session.Id, 2, 100));

        result.Success.Should().BeFalse();
        result.Error.Should().Be(PlaceBetError.AlreadyBet);
        db.Bets.Should().HaveCount(1);
    }

    [Fact]
    public async Task InsufficientChips_ReturnsError()
    {
        var (sut, db) = BuildSut();
        var (player, session) = await SeedAsync(db);

        var result = await sut.ExecuteAsync(new PlaceBetRequest(player.Id, session.Id, 1, 9999));

        result.Success.Should().BeFalse();
        result.Error.Should().Be(PlaceBetError.InsufficientChips);
    }

    [Fact]
    public async Task BettingClosed_ReturnsError()
    {
        var (sut, db) = BuildSut();
        var player = Player.Create("Tester", 1000);
        db.Players.Add(player);

        var session = GameSession.CreateNew();
        session.StartBetting(-1); // deadline already passed
        db.GameSessions.Add(session);
        await db.SaveChangesAsync();

        var result = await sut.ExecuteAsync(new PlaceBetRequest(player.Id, session.Id, 1, 100));

        result.Success.Should().BeFalse();
        result.Error.Should().Be(PlaceBetError.BettingClosed);
    }

    [Fact]
    public async Task InvalidNpcId_ReturnsError()
    {
        var (sut, db) = BuildSut();
        var (player, session) = await SeedAsync(db);

        var result = await sut.ExecuteAsync(new PlaceBetRequest(player.Id, session.Id, 99, 100));

        result.Success.Should().BeFalse();
        result.Error.Should().Be(PlaceBetError.InvalidNpcId);
    }
}
```

- [ ] **Step 3: Run tests**

```bash
dotnet test tests/CubeRacing.Tests --filter "FullyQualifiedName~PlaceBetTests"
```
Expected: `5 passed, 0 failed`.

- [ ] **Step 4: Run all tests**

```bash
dotnet test tests/CubeRacing.Tests
```
Expected: All 17 tests pass (`7 RaceSimulator + 5 SettlementCalculator + 5 PlaceBet`).

- [ ] **Step 5: Commit**

```bash
git add tests/CubeRacing.Tests
git commit -m "feat: add PlaceBet integration tests covering all edge cases"
```

---

## Self-Review Notes

**Spec coverage check:**

| Spec section | Covered by task |
|---|---|
| Anonymous player + token | Task 7 (CreatePlayer), Task 10 (Middleware) |
| Betting phase with 60s timer | Task 9 (GameSessionManager) |
| RabbitMQ phase transitions | Task 8 (all 4 consumers) |
| RaceSimulator + stacking | Task 3 (TDD) |
| Topmost NPC wins | Task 3 (`GetWinner()` returns `stack[^1]`) |
| Pari-mutuel odds | Task 4 (SettlementCalculator TDD) |
| SignalR broadcast all events | Task 9 (GameHubNotifier), Task 8 (consumers) |
| REST API (4 endpoints) | Task 10 (3 controllers) |
| `squareStacks` bottom-to-top | Task 3 (`List<int>[]` appends to end = top) |
| Leaderboard top 20 | Task 7 (GetLeaderboard) |
| Initial 1000 chips | Task 7 (CreatePlayer uses GameSettings.InitialChips) |
| EF Core + MSSQL | Task 6 |
| Clean Architecture layers | All tasks respect Domain→Application→Infrastructure→API |
| Domain unit tests | Tasks 3, 4 |
| PlaceBet integration tests | Task 12 |

**No gaps found.**
