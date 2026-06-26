# Race UX Improvements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement three race UX improvements: (1) 30-second pre-race delay with countdown, (2) step-by-step NPC animation with Unity parent-child stacking, (3) lobby bet indicator showing which NPC the player chose.

**Architecture:** Backend broadcasts a `RaceStarting` SignalR event with a UTC start timestamp before running rounds; frontend uses this for a countdown. Unity transform parent-child hierarchy replaces manual carried-NPC position management. Bet state is tracked in `GameStateService` and reflected in `NpcCardView` with a gold highlight and grey-out overlay.

**Tech Stack:** .NET 9, ASP.NET Core SignalR, Unity 6, VContainer, MessagePipe, R3 ReactiveProperty, DOTween, UniTask, Newtonsoft.Json (frontend), System.Text.Json (backend), xUnit + FluentAssertions + Moq (tests)

## Global Constraints

- All backend code: C# 12, .NET 9
- All frontend code: C# in Unity 6; MonoBehaviour tests not applicable — use `dotnet test` for backend, Unity Console errors for frontend
- Backend compile command (run from `backend/`): `dotnet build`
- Backend test command (run from `backend/`): `dotnet test`
- MessagePipe publishers/subscribers are auto-resolved by VContainer via `RegisterMessagePipe()` in `Main.cs` — no explicit per-type registration needed
- Never add using statements already present in the file
- `_board.GetSquarePosition(sq)` returns `Vector3` at Y=0; base height for NPC on ground = Y=0.5

---

## Task 1: Backend — Race Start Delay

**Files:**
- Modify: `backend/src/CubeRacing.Application/Config/GameSettings.cs`
- Modify: `backend/src/CubeRacing.Application/Interfaces/ICurrentSessionStore.cs`
- Modify: `backend/src/CubeRacing.Infrastructure/Services/CurrentSessionStore.cs`
- Modify: `backend/src/CubeRacing.Application/Interfaces/IGameHubNotifier.cs`
- Modify: `backend/src/CubeRacing.Infrastructure/Services/GameHubNotifier.cs`
- Modify: `backend/src/CubeRacing.Infrastructure/Messaging/Consumers/BettingEndedConsumer.cs`
- Modify: `backend/src/CubeRacing.Infrastructure/Services/GameSessionManager.cs`
- Modify: `backend/src/CubeRacing.Application/Dtos/CurrentSessionDto.cs`
- Modify: `backend/src/CubeRacing.Application/UseCases/GetCurrentSession.cs`
- Modify: `backend/src/CubeRacing.API/appsettings.json`
- Create: `backend/tests/CubeRacing.Tests/UseCases/GetCurrentSessionTests.cs`

**Interfaces:**
- Produces: `ICurrentSessionStore.RaceStartsAt: DateTime?` (readable by `GetCurrentSession`), `IGameHubNotifier.NotifyRaceStartingAsync`, `CurrentSessionDto.RaceStartsAt: DateTime?`

---

- [ ] **Step 1: Write the failing test**

Create `backend/tests/CubeRacing.Tests/UseCases/GetCurrentSessionTests.cs`:

```csharp
using CubeRacing.Application.Config;
using CubeRacing.Application.UseCases;
using CubeRacing.Domain.Entities;
using CubeRacing.Infrastructure.Persistence;
using CubeRacing.Infrastructure.Persistence.Repositories;
using CubeRacing.Infrastructure.Services;
using CubeRacing.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace CubeRacing.Tests.UseCases;

public class GetCurrentSessionTests
{
    [Fact]
    public async Task ExecuteAsync_IncludesRaceStartsAt_WhenStoreHasValue()
    {
        var db          = TestDbContextFactory.Create();
        var sessionRepo = new GameSessionRepository(db);
        var betRepo     = new BetRepository(db);
        var store       = new CurrentSessionStore();
        var settings    = Options.Create(new GameSettings { NpcCount = 4 });

        var session = GameSession.CreateNew(20);
        session.StartBetting(60);
        session.StartRacing();
        await sessionRepo.AddAsync(session, default);
        store.Set(session.Id);

        var raceStartsAt = DateTime.UtcNow.AddSeconds(25);
        store.RaceStartsAt = raceStartsAt;

        var useCase = new GetCurrentSession(store, sessionRepo, betRepo, settings);
        var result  = await useCase.ExecuteAsync();

        result.Should().NotBeNull();
        result!.RaceStartsAt.Should().BeCloseTo(raceStartsAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNullRaceStartsAt_WhenStoreIsEmpty()
    {
        var db          = TestDbContextFactory.Create();
        var sessionRepo = new GameSessionRepository(db);
        var betRepo     = new BetRepository(db);
        var store       = new CurrentSessionStore();
        var settings    = Options.Create(new GameSettings { NpcCount = 4 });

        var session = GameSession.CreateNew(20);
        session.StartBetting(60);
        await sessionRepo.AddAsync(session, default);
        store.Set(session.Id);
        // store.RaceStartsAt not set → should be null

        var useCase = new GetCurrentSession(store, sessionRepo, betRepo, settings);
        var result  = await useCase.ExecuteAsync();

        result.Should().NotBeNull();
        result!.RaceStartsAt.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run test to confirm it fails**

```bash
cd backend && dotnet test --filter "FullyQualifiedName~GetCurrentSessionTests" -v
```

Expected: compile error — `CurrentSessionStore` has no `RaceStartsAt` property and `CurrentSessionDto` has no such field.

- [ ] **Step 3: Add `RaceStartDelaySeconds` to `GameSettings`**

Full file `backend/src/CubeRacing.Application/Config/GameSettings.cs`:

```csharp
namespace CubeRacing.Application.Config;

public class GameSettings
{
    public int    MapLength             { get; init; } = 20;
    public int    NpcCount             { get; init; } = 4;
    public int    BettingDurationSeconds { get; init; } = 60;
    public int    WaitingDurationSeconds { get; init; } = 5;
    public int    RaceStartDelaySeconds  { get; init; } = 30;
    public int    RoundIntervalMs        { get; init; } = 1500;
    public int    InitialChips           { get; init; } = 1000;
    public double DefaultOdds            { get; init; } = 1.3;
}
```

- [ ] **Step 4: Add `RaceStartsAt` to `ICurrentSessionStore` and `CurrentSessionStore`**

Full file `backend/src/CubeRacing.Application/Interfaces/ICurrentSessionStore.cs`:

```csharp
namespace CubeRacing.Application.Interfaces;

public interface ICurrentSessionStore
{
    Guid?     CurrentSessionId { get; }
    DateTime? RaceStartsAt     { get; set; }
    void Set(Guid sessionId);
}
```

Full file `backend/src/CubeRacing.Infrastructure/Services/CurrentSessionStore.cs`:

```csharp
using CubeRacing.Application.Interfaces;

namespace CubeRacing.Infrastructure.Services;

public class CurrentSessionStore : ICurrentSessionStore
{
    private Guid? _id;
    public Guid?     CurrentSessionId => _id;
    public DateTime? RaceStartsAt     { get; set; }
    public void Set(Guid sessionId) => _id = sessionId;
}
```

- [ ] **Step 5: Add `RaceStartsAt` to `CurrentSessionDto` and `GetCurrentSession`**

Full file `backend/src/CubeRacing.Application/Dtos/CurrentSessionDto.cs`:

```csharp
namespace CubeRacing.Application.Dtos;

public record CurrentSessionDto(
    Guid              SessionId,
    string            Status,
    int?              BettingSecondsRemaining,
    List<NpcOddsDto>  NpcOdds,
    int               MapLength,
    DateTime?         RaceStartsAt = null);
```

In `backend/src/CubeRacing.Application/UseCases/GetCurrentSession.cs`, change only the final `return` line:

```csharp
return new CurrentSessionDto(
    session.Id, session.Status.ToString(), remaining, npcOdds,
    session.MapLength, _store.RaceStartsAt);
```

- [ ] **Step 6: Add `NotifyRaceStartingAsync` to hub notifier**

Full file `backend/src/CubeRacing.Application/Interfaces/IGameHubNotifier.cs`:

```csharp
using CubeRacing.Domain.Events;

namespace CubeRacing.Application.Interfaces;

public interface IGameHubNotifier
{
    Task NotifyBettingStartedAsync(Guid sessionId);
    Task NotifyOddsUpdatedAsync(Guid sessionId, object odds);
    Task NotifyBettingEndedAsync(Guid sessionId);
    Task NotifyRaceStartingAsync(Guid sessionId, DateTime raceStartsAt);
    Task NotifyRoundExecutedAsync(Guid sessionId, RoundExecutedEvent round);
    Task NotifyRaceCompletedAsync(Guid sessionId, int winnerNpcId);
    Task NotifySettlementDoneAsync(Guid sessionId, object result);
}
```

Full file `backend/src/CubeRacing.Infrastructure/Services/GameHubNotifier.cs`:

```csharp
using CubeRacing.Application.Interfaces;
using CubeRacing.Domain.Events;
using CubeRacing.Infrastructure.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CubeRacing.Infrastructure.Services;

public class GameHubNotifier : IGameHubNotifier
{
    private readonly IHubContext<GameHub> _hub;
    public GameHubNotifier(IHubContext<GameHub> hub) => _hub = hub;

    public Task NotifyBettingStartedAsync(Guid sessionId)
        => _hub.Clients.All.SendAsync("BettingStarted", new { sessionId });

    public Task NotifyOddsUpdatedAsync(Guid sessionId, object odds)
        => _hub.Clients.Group(sessionId.ToString()).SendAsync("OddsUpdated", odds);

    public Task NotifyBettingEndedAsync(Guid sessionId)
        => _hub.Clients.Group(sessionId.ToString()).SendAsync("BettingEnded");

    public Task NotifyRaceStartingAsync(Guid sessionId, DateTime raceStartsAt)
        => _hub.Clients.Group(sessionId.ToString())
               .SendAsync("RaceStarting", new { sessionId, raceStartsAt });

    public Task NotifyRoundExecutedAsync(Guid sessionId, RoundExecutedEvent round)
        => _hub.Clients.Group(sessionId.ToString()).SendAsync("RoundExecuted", round);

    public Task NotifyRaceCompletedAsync(Guid sessionId, int winnerNpcId)
        => _hub.Clients.Group(sessionId.ToString()).SendAsync("RaceCompleted", new { winnerNpcId });

    public Task NotifySettlementDoneAsync(Guid sessionId, object result)
        => _hub.Clients.Group(sessionId.ToString()).SendAsync("SettlementDone", result);
}
```

- [ ] **Step 7: Update `BettingEndedConsumer` to add delay and notify**

`BettingEndedConsumer` needs `ICurrentSessionStore` injected (add to constructor). Full file `backend/src/CubeRacing.Infrastructure/Messaging/Consumers/BettingEndedConsumer.cs`:

```csharp
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
    private readonly IServiceScopeFactory  _scopeFactory;
    private readonly IMessagePublisher     _publisher;
    private readonly ICurrentSessionStore  _store;
    private readonly GameSettings          _settings;

    public BettingEndedConsumer(
        IConnectionFactory factory,
        IServiceScopeFactory scopeFactory,
        IMessagePublisher publisher,
        ICurrentSessionStore store,
        IOptions<GameSettings> settings)
        : base(factory, "betting.ended")
    {
        _scopeFactory = scopeFactory;
        _publisher    = publisher;
        _store        = store;
        _settings     = settings.Value;
    }

    protected override async Task HandleAsync(string json, CancellationToken ct)
    {
        var ev = JsonSerializer.Deserialize<BettingEndedEvent>(json)!;

        using var scope      = _scopeFactory.CreateScope();
        var sessionRepo      = scope.ServiceProvider.GetRequiredService<IGameSessionRepository>();
        var roundRepo        = scope.ServiceProvider.GetRequiredService<IGameRoundRepository>();
        var hubNotifier      = scope.ServiceProvider.GetRequiredService<IGameHubNotifier>();

        var session = await sessionRepo.GetByIdAsync(ev.SessionId, ct);
        if (session is null) return;

        session.StartRacing();
        await sessionRepo.UpdateAsync(session, ct);
        await hubNotifier.NotifyBettingEndedAsync(ev.SessionId);

        // Compute and broadcast pre-race start time, then wait
        var raceStartsAt = DateTime.UtcNow.AddSeconds(_settings.RaceStartDelaySeconds);
        _store.RaceStartsAt = raceStartsAt;
        await hubNotifier.NotifyRaceStartingAsync(ev.SessionId, raceStartsAt);
        await Task.Delay(_settings.RaceStartDelaySeconds * 1000, ct);

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
                result.Actions.Select(a => new RoundActionDto(
                    a.NpcId, a.DiceRoll, a.FromSquare, a.ToSquare, a.CarriedNpcIds)).ToList(),
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

- [ ] **Step 8: Clear `RaceStartsAt` in `GameSessionManager` at session start**

In `backend/src/CubeRacing.Infrastructure/Services/GameSessionManager.cs`, add `_store.RaceStartsAt = null;` as the first line of `RunSessionLifecycleAsync`:

```csharp
private async Task RunSessionLifecycleAsync(CancellationToken ct)
{
    _store.RaceStartsAt = null;   // ← add this line

    // Create session
    using var scope = _scopeFactory.CreateScope();
    // ... rest of existing method unchanged ...
```

- [ ] **Step 9: Add `RaceStartDelaySeconds` to `appsettings.json`**

In `backend/src/CubeRacing.API/appsettings.json`, inside `"GameSettings"`:

```json
"GameSettings": {
  "MapLength": 20, "NpcCount": 4,
  "BettingDurationSeconds": 60, "WaitingDurationSeconds": 5,
  "RaceStartDelaySeconds": 30,
  "RoundIntervalMs": 1500, "InitialChips": 1000, "DefaultOdds": 1.3
},
```

- [ ] **Step 10: Run tests and build**

```bash
cd backend && dotnet test --filter "FullyQualifiedName~GetCurrentSessionTests" -v
```

Expected: both tests PASS.

```bash
cd backend && dotnet build
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 11: Commit**

```bash
git add backend/src/CubeRacing.Application/Config/GameSettings.cs \
        backend/src/CubeRacing.Application/Interfaces/ICurrentSessionStore.cs \
        backend/src/CubeRacing.Infrastructure/Services/CurrentSessionStore.cs \
        backend/src/CubeRacing.Application/Interfaces/IGameHubNotifier.cs \
        backend/src/CubeRacing.Infrastructure/Services/GameHubNotifier.cs \
        backend/src/CubeRacing.Infrastructure/Messaging/Consumers/BettingEndedConsumer.cs \
        backend/src/CubeRacing.Infrastructure/Services/GameSessionManager.cs \
        backend/src/CubeRacing.Application/Dtos/CurrentSessionDto.cs \
        backend/src/CubeRacing.Application/UseCases/GetCurrentSession.cs \
        backend/src/CubeRacing.API/appsettings.json \
        backend/tests/CubeRacing.Tests/UseCases/GetCurrentSessionTests.cs
git commit -m "feat: add 30s pre-race delay and RaceStarting SignalR notification"
```

---

## Task 2: Frontend — Race Start Countdown

**Files:**
- Modify: `frontend/unity/Assets/Scripts/Core/Messages.cs`
- Modify: `frontend/unity/Assets/Scripts/Core/Dtos.cs`
- Modify: `frontend/unity/Assets/Scripts/Core/GameStateService.cs`
- Modify: `frontend/unity/Assets/Scripts/Network/SignalRClient.cs`
- Modify: `frontend/unity/Assets/Scripts/Race/RacePresenter.cs`

**Interfaces:**
- Consumes: `CurrentSessionDto.RaceStartsAt` (Task 1), `ICurrentSessionStore.RaceStartsAt` (Task 1)
- Produces: `GameStateService.RaceStartsAt: ReactiveProperty<DateTime?>`

---

- [ ] **Step 1: Add `RaceStartingMessage` to `Messages.cs`**

Add to end of `frontend/unity/Assets/Scripts/Core/Messages.cs` (before the closing `}`):

```csharp
    public readonly struct RaceStartingMessage
    {
        public readonly DateTime RaceStartsAt;
        public RaceStartingMessage(DateTime raceStartsAt) => RaceStartsAt = raceStartsAt;
    }
```

- [ ] **Step 2: Add `raceStartsAt` to `CurrentSessionResponse` in `Dtos.cs`**

In `frontend/unity/Assets/Scripts/Core/Dtos.cs`, add one field to `CurrentSessionResponse`:

```csharp
[Serializable]
public class CurrentSessionResponse
{
    public Guid             sessionId;
    public string           status;
    public int?             bettingSecondsRemaining;
    public List<NpcOddsDto> npcOdds;
    public int              mapLength;
    public DateTime?        raceStartsAt;   // ← add this field
}
```

- [ ] **Step 3: Update `GameStateService` — add `RaceStartsAt` property and subscriptions**

In `frontend/unity/Assets/Scripts/Core/GameStateService.cs`:

**Add two new constructor parameters** (after `settlementSubscriber`):
```csharp
private readonly ISubscriber<RaceStartingMessage>  _raceStartingSubscriber;
private readonly ISubscriber<BettingStartedMessage> _bettingStartedSubscriber;
```

**Update the constructor signature** to include them:
```csharp
public GameStateService(
    ISubscriber<OddsUpdatedMessage>    oddsSubscriber,
    ISubscriber<BettingEndedMessage>   bettingEndedSubscriber,
    ISubscriber<RaceCompletedMessage>  raceCompletedSubscriber,
    ISubscriber<SettlementDoneMessage> settlementSubscriber,
    ISubscriber<RaceStartingMessage>   raceStartingSubscriber,
    ISubscriber<BettingStartedMessage> bettingStartedSubscriber)
{
    _oddsSubscriber           = oddsSubscriber;
    _bettingEndedSubscriber   = bettingEndedSubscriber;
    _raceCompletedSubscriber  = raceCompletedSubscriber;
    _settlementSubscriber     = settlementSubscriber;
    _raceStartingSubscriber   = raceStartingSubscriber;
    _bettingStartedSubscriber = bettingStartedSubscriber;
}
```

**Add `RaceStartsAt` property** after `IsConnected`:
```csharp
public ReactiveProperty<DateTime?> RaceStartsAt { get; } = new(null);
```

**Add two subscriptions in `Initialize()`**:
```csharp
_raceStartingSubscriber.Subscribe(m =>
    RaceStartsAt.Value = m.RaceStartsAt).AddTo(_bag);

_bettingStartedSubscriber.Subscribe(_ =>
    RaceStartsAt.Value = null).AddTo(_bag);
```

- [ ] **Step 4: Handle `"RaceStarting"` in `SignalRClient`**

In `frontend/unity/Assets/Scripts/Network/SignalRClient.cs`:

**Add field and constructor parameter** for the new publisher:
```csharp
private readonly IPublisher<RaceStartingMessage> _raceStartingPublisher;
```

Add to constructor (after `bettingEndedPublisher`):
```csharp
IPublisher<RaceStartingMessage>    raceStartingPublisher,
```

Assign in constructor body:
```csharp
_raceStartingPublisher = raceStartingPublisher;
```

**Add case in `ProcessMessage` switch**, between `"BettingStarted"` and `"OddsUpdated"`:
```csharp
case "RaceStarting":
    if (args?.Count > 0)
    {
        var raceStartsAt = args[0]["raceStartsAt"].Value<DateTime>();
        _raceStartingPublisher.Publish(new RaceStartingMessage(raceStartsAt));
    }
    break;
```

- [ ] **Step 5: Add countdown logic to `RacePresenter`**

In `frontend/unity/Assets/Scripts/Race/RacePresenter.cs`:

**Add using** at top:
```csharp
using System;
```
(Only if not already present.)

**Add field**:
```csharp
private CancellationTokenSource _countdownCts;
```

**In `Start()`, after `SyncNpcPositionsAsync` call**, add subscription:
```csharp
_gameState.RaceStartsAt
    .Where(t => t.HasValue && t.Value > DateTime.UtcNow)
    .Subscribe(t => StartCountdown(t!.Value))
    .AddTo(_disposables);
```

**In `OnDestroy()`**, add:
```csharp
_countdownCts?.Cancel();
```

**Add two new methods** after `SyncNpcPositionsAsync`:

```csharp
private void StartCountdown(DateTime raceStartsAt)
{
    _countdownCts?.Cancel();
    _countdownCts = new CancellationTokenSource();
    CountdownAsync(raceStartsAt, _countdownCts.Token).Forget();
}

private async UniTaskVoid CountdownAsync(DateTime raceStartsAt, CancellationToken ct)
{
    while (!ct.IsCancellationRequested && DateTime.UtcNow < raceStartsAt)
    {
        int remaining = Math.Max(0, (int)(raceStartsAt - DateTime.UtcNow).TotalSeconds);
        _statusText.text = $"The racing will begin in {remaining} seconds....";
        await UniTask.Delay(1000, cancellationToken: ct);
    }
    // Status text reverts to GameStateService.Status subscription once countdown ends
}
```

**Update `SyncNpcPositionsAsync`** to fall back to API for `raceStartsAt` if the SignalR event was missed:

```csharp
private async UniTaskVoid SyncNpcPositionsAsync(CancellationToken ct)
{
    try
    {
        var stacks = await _api.GetCurrentSquaresAsync(ct);
        ApplySquareStacks(stacks);

        if (_gameState.RaceStartsAt.Value == null)
        {
            var session = await _api.GetCurrentSessionAsync(ct);
            if (session != null
                && session.raceStartsAt.HasValue
                && session.raceStartsAt.Value > DateTime.UtcNow)
            {
                _gameState.RaceStartsAt.Value = session.raceStartsAt.Value;
            }
        }
    }
    catch (ApiException ex) when (ex.StatusCode == 204 || ex.StatusCode == 404) { }
    catch (OperationCanceledException) { }
    catch (Exception e) { Debug.LogWarning($"[Race] Sync failed: {e.Message}"); }
}
```

- [ ] **Step 6: Verify Unity compilation**

Open Unity Editor. Check the Console window — no compile errors. If errors appear, fix them before continuing.

- [ ] **Step 7: Commit**

```bash
git add frontend/unity/Assets/Scripts/Core/Messages.cs \
        frontend/unity/Assets/Scripts/Core/Dtos.cs \
        frontend/unity/Assets/Scripts/Core/GameStateService.cs \
        frontend/unity/Assets/Scripts/Network/SignalRClient.cs \
        frontend/unity/Assets/Scripts/Race/RacePresenter.cs
git commit -m "feat: show race start countdown in race scene (F1 frontend)"
```

---

## Task 3: Step-by-Step Animation + Parent-Child Hierarchy

**Files:**
- Modify: `frontend/unity/Assets/Scripts/Race/NpcCubeController.cs`
- Modify: `frontend/unity/Assets/Scripts/Race/RacePresenter.cs`

**Interfaces:**
- Produces: `NpcCubeController.NpcId: int`, `NpcCubeController.MoveToAsync(Vector3, float, CancellationToken)`

---

- [ ] **Step 1: Add `NpcId` property and update `MoveToAsync` in `NpcCubeController`**

Full file `frontend/unity/Assets/Scripts/Race/NpcCubeController.cs`:

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

namespace CubeRacing
{
    public class NpcCubeController : MonoBehaviour
    {
        public int NpcId { get; private set; }

        public void Initialize(int npcId, Color color)
        {
            NpcId = npcId;
            var renderer = GetComponent<Renderer>();
            if (renderer != null) renderer.material.color = color;
        }

        public async UniTask MoveToAsync(Vector3 target, float duration, CancellationToken ct = default)
        {
            await transform.DOJump(target, jumpPower: 1.2f, numJumps: 1, duration: duration)
                           .SetEase(Ease.InOutSine)
                           .ToUniTask(cancellationToken: ct);
        }
    }
}
```

> `SetStackOffset` is removed — it is no longer used; stacking is now handled by the parent-child hierarchy.

- [ ] **Step 2: Add `_localStacks` field and rewrite `ApplySquareStacks` in `RacePresenter`**

In `frontend/unity/Assets/Scripts/Race/RacePresenter.cs`:

**Add field** after `_roundQueue`:
```csharp
private readonly Dictionary<int, List<int>> _localStacks = new();
```

**Replace the full `ApplySquareStacks` method**:

```csharp
private void ApplySquareStacks(Dictionary<string, List<int>> stacks)
{
    if (stacks == null) return;

    foreach (var cube in _board.NpcCubes.Values)
        cube.transform.SetParent(null);

    _localStacks.Clear();

    foreach (var (squareStr, npcIds) in stacks)
    {
        if (!int.TryParse(squareStr, out int sq)) continue;
        _localStacks[sq] = new List<int>(npcIds);
        var sqPos = _board.GetSquarePosition(sq);

        for (int i = 0; i < npcIds.Count; i++)
        {
            if (!_board.NpcCubes.TryGetValue(npcIds[i], out var cube)) continue;
            if (i == 0)
            {
                cube.transform.SetParent(null);
                cube.transform.position = sqPos + Vector3.up * 0.5f;
            }
            else
            {
                var below = _board.NpcCubes[npcIds[i - 1]];
                cube.transform.SetParent(below.transform);
                cube.transform.localPosition = Vector3.up * 0.5f;
            }
        }
    }
}
```

- [ ] **Step 3: Rewrite `PlayRoundAsync` with step-by-step movement**

**Replace the full `PlayRoundAsync` method**:

```csharp
private async UniTask PlayRoundAsync(RoundExecutedPayload payload, CancellationToken ct)
{
    if (payload.actions == null || payload.actions.Count == 0) return;

    float durationPerAction  = GameSettings.RoundIntervalMs / 1000f * 0.8f / payload.actions.Count;
    bool  hadValidationError = false;

    foreach (var action in payload.actions)
    {
        int steps = action.toSquare - action.fromSquare;
        if (steps <= 0) continue;

        float stepDuration = durationPerAction / steps;

        if (!_board.NpcCubes.TryGetValue(action.npcId, out var movingCube)) continue;

        // Remove the moving group from fromSquare tracking
        if (_localStacks.TryGetValue(action.fromSquare, out var fromList))
        {
            fromList.Remove(action.npcId);
            foreach (var cId in action.carriedNpcIds)
                fromList.Remove(cId);
        }

        // De-parent the moving NPC; its carried descendants follow automatically
        movingCube.transform.SetParent(null);

        for (int sq = action.fromSquare + 1; sq <= action.toSquare; sq++)
        {
            int stackCount  = _localStacks.TryGetValue(sq, out var existing) ? existing.Count : 0;
            var targetWorld = _board.GetSquarePosition(sq) + Vector3.up * (0.5f + stackCount * 0.5f);

            await movingCube.MoveToAsync(targetWorld, stepDuration, ct);

            if (stackCount > 0)
            {
                var topNpc = _board.NpcCubes[_localStacks[sq][^1]];
                movingCube.transform.SetParent(topNpc.transform);
            }

            if (sq != action.toSquare)
                movingCube.transform.SetParent(null);
        }

        // Update tracking with final position
        if (!_localStacks.TryGetValue(action.toSquare, out var toList))
            _localStacks[action.toSquare] = toList = new List<int>();
        toList.Add(action.npcId);
        toList.AddRange(action.carriedNpcIds);

        // Validate descendants match carriedNpcIds
        var actualDescendants = GetAllDescendantIds(action.npcId);
        var expectedSet       = new HashSet<int>(action.carriedNpcIds);
        var actualSet         = new HashSet<int>(actualDescendants);
        if (!expectedSet.SetEquals(actualSet))
        {
            Debug.LogError($"[Race] Stack mismatch NPC {action.npcId}. " +
                           $"Expected: [{string.Join(",", expectedSet)}] " +
                           $"Actual: [{string.Join(",", actualSet)}]");
            hadValidationError = true;
        }
    }

    // Re-sync _localStacks from authoritative backend data
    _localStacks.Clear();
    foreach (var (k, v) in payload.squareStacks)
        if (int.TryParse(k, out int sq))
            _localStacks[sq] = new List<int>(v);

    // Snap positions if validation detected drift
    if (hadValidationError)
        ApplySquareStacks(payload.squareStacks);
}
```

- [ ] **Step 4: Add descendant-collection helpers to `RacePresenter`**

Add after `PlayRoundAsync`:

```csharp
private List<int> GetAllDescendantIds(int npcId)
{
    var result = new List<int>();
    if (_board.NpcCubes.TryGetValue(npcId, out var cube))
        CollectDescendants(cube.transform, result);
    return result;
}

private static void CollectDescendants(Transform t, List<int> result)
{
    foreach (Transform child in t)
    {
        var ctrl = child.GetComponent<NpcCubeController>();
        if (ctrl != null) result.Add(ctrl.NpcId);
        CollectDescendants(child, result);
    }
}
```

- [ ] **Step 5: Verify Unity compilation**

Open Unity Editor. Confirm no Console errors after domain reload.

- [ ] **Step 6: Commit**

```bash
git add frontend/unity/Assets/Scripts/Race/NpcCubeController.cs \
        frontend/unity/Assets/Scripts/Race/RacePresenter.cs
git commit -m "feat: step-by-step NPC animation with Unity parent-child stacking (F2)"
```

---

## Task 4: Lobby Bet Indicator

**Files:**
- Modify: `frontend/unity/Assets/Scripts/Core/GameStateService.cs`
- Modify: `frontend/unity/Assets/Scripts/Lobby/BettingDialogPresenter.cs`
- Modify: `frontend/unity/Assets/Scripts/Lobby/NpcCardView.cs`
- Modify: `frontend/unity/Assets/Scripts/Lobby/LobbyPresenter.cs`
- Unity Editor: `NpcCardView` prefab — add three new child UI components

**Interfaces:**
- Consumes: `GameStateService.RaceStartsAt` (Task 2), `GameStateService.BetNpcId`, `GameStateService.BetAmount`
- Produces: `GameStateService.BetNpcId: ReactiveProperty<int?>`, `GameStateService.BetAmount: ReactiveProperty<int?>`, `GameStateService.SetBet(int, int)`, `NpcCardView.SetBetHighlight(int)`, `NpcCardView.SetGreyedOut(bool)`

---

- [ ] **Step 1: Add `BetNpcId`, `BetAmount`, `SetBet()` to `GameStateService`**

In `frontend/unity/Assets/Scripts/Core/GameStateService.cs`:

**Add two properties** after `RaceStartsAt`:
```csharp
public ReactiveProperty<int?> BetNpcId  { get; } = new(null);
public ReactiveProperty<int?> BetAmount { get; } = new(null);
```

**Add `SetBet` method** after `ApplySession`:
```csharp
public void SetBet(int npcId, int amount)
{
    BetNpcId.Value  = npcId;
    BetAmount.Value = amount;
}
```

**In `Initialize()`**, extend the existing `_bettingStartedSubscriber` subscription (the one added in Task 2 that clears `RaceStartsAt`) to also clear bet state. Replace it with:
```csharp
_bettingStartedSubscriber.Subscribe(_ =>
{
    RaceStartsAt.Value = null;
    BetNpcId.Value     = null;
    BetAmount.Value    = null;
}).AddTo(_bag);
```

- [ ] **Step 2: Call `SetBet` in `BettingDialogPresenter` after successful bet**

In `frontend/unity/Assets/Scripts/Lobby/BettingDialogPresenter.cs`, in `ConfirmAsync`, after the line `_gameState.HasPlacedBet.Value = true;`, add:

```csharp
_gameState.SetBet(_currentNpcId, _amount);
```

- [ ] **Step 3: Add UI fields and methods to `NpcCardView`**

Full file `frontend/unity/Assets/Scripts/Lobby/NpcCardView.cs`:

```csharp
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CubeRacing
{
    public class NpcCardView : MonoBehaviour
    {
        [SerializeField] private Image    _colorBlock;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _oddsText;
        [SerializeField] private Button   _betButton;
        [SerializeField] private TMP_Text _betButtonText;

        // Assigned in Unity Editor (added to prefab in Task 4 Step 4)
        [SerializeField] private Image    _betBorder;
        [SerializeField] private TMP_Text _betBadgeText;
        [SerializeField] private Image    _greyOverlay;

        public event Action<int> OnBetClicked;
        private int _npcId;

        private void Awake()
        {
            _betButton.onClick.AddListener(() => OnBetClicked?.Invoke(_npcId));
        }

        public void SetNpc(NpcEntry entry, double odds)
        {
            _npcId              = entry.id;
            _colorBlock.color   = entry.color;
            _nameText.text      = entry.npcName;
            _oddsText.text      = $"{odds:F1}x";
            _betButtonText.text = "Bet";
        }

        public void UpdateOdds(double odds) => _oddsText.text = $"{odds:F1}x";

        public void SetBettingEnabled(bool enabled) => _betButton.interactable = enabled;

        public void SetBetPlaced(bool placed)
        {
            _betButton.interactable = false;
            if (placed) _betButtonText.text = "Bet Placed";
        }

        public void SetBetHighlight(int amount)
        {
            if (_betBorder != null)
                _betBorder.color = new Color(1f, 0.84f, 0f);   // gold
            if (_betBadgeText != null)
            {
                _betBadgeText.text    = $"✓ Bet {amount:N0}";
                _betBadgeText.gameObject.SetActive(true);
            }
        }

        public void SetGreyedOut(bool greyed)
        {
            if (_greyOverlay != null)
                _greyOverlay.gameObject.SetActive(greyed);
        }

        public void ResetBetVisuals()
        {
            if (_betBorder != null)
                _betBorder.color = Color.clear;
            if (_betBadgeText != null)
                _betBadgeText.gameObject.SetActive(false);
            SetGreyedOut(false);
        }
    }
}
```

- [ ] **Step 4: Add required UI components to NpcCardView prefab in Unity Editor**

Locate the `NpcCardView` prefab (search in Project window: `NpcCardView`). Open it for editing, then:

1. **`_betBorder`** — Add a child `Image` GameObject named `BetBorder`. Set its color to `Color.clear` (alpha 0) by default. Configure RectTransform to cover the full card (Anchor: stretch-stretch, all offsets 0). Set `Image Type` to `Sliced` with a border-style sprite, or just use a solid color with a wide outline effect. Assign to `NpcCardView._betBorder` field in Inspector.

2. **`_betBadgeText`** — Add a child `TMP_Text` GameObject named `BetBadge`. Set default `Active = false`. Position it at the top of the card. Set font size ~14, bold, color white. Assign to `NpcCardView._betBadgeText` in Inspector.

3. **`_greyOverlay`** — Add a child `Image` GameObject named `GreyOverlay`. Set color to `(0, 0, 0, 0.55)` (semi-transparent black/grey). RectTransform: stretch-stretch, all offsets 0. Set `Raycast Target = false`. Set default `Active = false`. Assign to `NpcCardView._greyOverlay` in Inspector.

> All three child objects should be placed at the top of the hierarchy inside NpcCardView so they render above content, or adjust sibling order via the Inspector.

Save the prefab.

- [ ] **Step 5: Subscribe to `BetNpcId` in `LobbyPresenter`**

In `frontend/unity/Assets/Scripts/Lobby/LobbyPresenter.cs`, in `Start()`, add this subscription after the existing `_gameState.HasPlacedBet.Subscribe(...)` line:

```csharp
_gameState.BetNpcId.Subscribe(npcId =>
{
    foreach (var card in _cards.Values)
        card.ResetBetVisuals();

    if (npcId.HasValue && _cards.TryGetValue(npcId.Value, out var betCard))
    {
        betCard.SetBetHighlight(_gameState.BetAmount.CurrentValue ?? 0);
        foreach (var (id, card) in _cards)
            if (id != npcId.Value) card.SetGreyedOut(true);
    }
}).AddTo(_disposables);
```

- [ ] **Step 6: Verify Unity compilation**

Open Unity Editor. Confirm no Console errors. If `_betBorder`, `_betBadgeText`, or `_greyOverlay` fields show null warnings at runtime, verify the prefab wiring in Step 4.

- [ ] **Step 7: Commit**

```bash
git add frontend/unity/Assets/Scripts/Core/GameStateService.cs \
        frontend/unity/Assets/Scripts/Lobby/BettingDialogPresenter.cs \
        frontend/unity/Assets/Scripts/Lobby/NpcCardView.cs \
        frontend/unity/Assets/Scripts/Lobby/LobbyPresenter.cs
git commit -m "feat: lobby bet indicator — highlight chosen NPC card and grey out others (F3)"
```

> Note: The Unity scene/prefab files (`.unity`, `.prefab`) will appear as modified in `git status`. Stage and commit them together with the script changes.
