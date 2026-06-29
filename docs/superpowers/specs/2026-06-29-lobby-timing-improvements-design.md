# Lobby Timing Improvements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix two UX problems — lobby shows no entry-window countdown before race starts, and backend finishes race so fast that returning viewers miss the next betting window.

**Architecture:** Two parallel changes: (1) backend broadcasts a new `WaitingStarted` SignalR event with `bettingStartsAt` timestamp so the lobby can count down to the next betting window; (2) lobby subscribes to the existing `RaceStartsAt` property to display a "Welcome" countdown during the 30-second race entry window. Config values (`WaitingDurationSeconds`, `RoundIntervalMs`) are already in `appsettings.json` and only need updated values.

**Tech Stack:** .NET 9 / SignalR / Unity 6 / C# / R3 ReactiveProperty / MessagePipe

## Global Constraints

- No new NuGet packages. No new Unity packages.
- `WaitingDurationSeconds` new default: **30**. `RoundIntervalMs` new default: **3000**.
- All `appsettings.json` values already override code defaults — code defaults must also be updated to stay consistent.
- New SignalR event name: `"WaitingStarted"`. Payload: `{ bettingStartsAt: DateTime (UTC) }`.
- `BettingStartsAt` is stored in `ICurrentSessionStore` (singleton) and exposed via `GET /api/sessions/current` so late-joiners get the value without waiting for the event.
- Frontend status text strings (exact):
  - Waiting phase: `"Next race in {N}s..."`
  - Race entry window: `"Welcome to watch the race! Will begin in {N}s..."`
  - Race in progress (entry window elapsed): `"Race in progress"`
- `GameStateService.BettingStartsAt` is cleared to `null` when `BettingStartedMessage` fires (same clearing rule as `RaceStartsAt`).
- `GameStateService.RaceStartsAt` is already cleared on `BettingStartedMessage` — no change needed there.

---

### Task 1: Update config defaults

**Files:**
- Modify: `backend/src/CubeRacing.Application/Config/GameSettings.cs`
- Modify: `backend/src/CubeRacing.API/appsettings.json`

**Interfaces:**
- Produces: `WaitingDurationSeconds = 30`, `RoundIntervalMs = 3000` — used by Tasks 2 and 3 indirectly through config injection.

- [ ] **Step 1: Update code defaults in GameSettings.cs**

Change lines 8 and 11:

```csharp
public int WaitingDurationSeconds { get; init; } = 30;   // was 5
public int RoundIntervalMs        { get; init; } = 3000;  // was 1500
```

- [ ] **Step 2: Update appsettings.json**

```json
"WaitingDurationSeconds": 30,
"RoundIntervalMs": 3000
```

- [ ] **Step 3: Build to verify no compile errors**

```bash
cd backend && dotnet build
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run all tests**

```bash
dotnet test
```
Expected: All 24 tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/CubeRacing.Application/Config/GameSettings.cs \
        backend/src/CubeRacing.API/appsettings.json
git commit -m "config: increase WaitingDurationSeconds to 30s and RoundIntervalMs to 3000ms"
```

---

### Task 2: Backend — WaitingStarted event

Add `BettingStartsAt` to the session store + DTO, and broadcast `WaitingStarted` from `GameSessionManager`.

**Files:**
- Modify: `backend/src/CubeRacing.Application/Interfaces/ICurrentSessionStore.cs`
- Modify: `backend/src/CubeRacing.Application/Interfaces/IGameHubNotifier.cs`
- Modify: `backend/src/CubeRacing.Application/Dtos/CurrentSessionDto.cs`
- Modify: `backend/src/CubeRacing.Infrastructure/Services/CurrentSessionStore.cs`
- Modify: `backend/src/CubeRacing.Infrastructure/Services/GameHubNotifier.cs`
- Modify: `backend/src/CubeRacing.Infrastructure/Services/GameSessionManager.cs`
- Modify: `backend/src/CubeRacing.Application/UseCases/GetCurrentSession.cs`

**Interfaces:**
- Consumes: `GameSettings.WaitingDurationSeconds` (from Task 1)
- Produces: SignalR event `"WaitingStarted"` with payload `{ bettingStartsAt: DateTime }`, `CurrentSessionDto.BettingStartsAt` field

- [ ] **Step 1: Add BettingStartsAt to ICurrentSessionStore**

```csharp
public interface ICurrentSessionStore
{
    Guid?     CurrentSessionId { get; }
    DateTime? RaceStartsAt     { get; set; }
    DateTime? BettingStartsAt  { get; set; }   // new
    void Set(Guid sessionId);
}
```

- [ ] **Step 2: Add BettingStartsAt to CurrentSessionStore**

```csharp
public class CurrentSessionStore : ICurrentSessionStore
{
    private Guid? _id;
    public Guid?     CurrentSessionId => _id;
    public DateTime? RaceStartsAt     { get; set; }
    public DateTime? BettingStartsAt  { get; set; }   // new
    public void Set(Guid sessionId) => _id = sessionId;
}
```

- [ ] **Step 3: Add NotifyWaitingStartedAsync to IGameHubNotifier**

```csharp
Task NotifyWaitingStartedAsync(Guid sessionId, DateTime bettingStartsAt);
```

- [ ] **Step 4: Implement NotifyWaitingStartedAsync in GameHubNotifier**

```csharp
public Task NotifyWaitingStartedAsync(Guid sessionId, DateTime bettingStartsAt)
    => _hub.Clients.Group(sessionId.ToString())
           .SendAsync("WaitingStarted", new { bettingStartsAt });
```

- [ ] **Step 5: Add BettingStartsAt to CurrentSessionDto**

```csharp
public record CurrentSessionDto(
    Guid              SessionId,
    string            Status,
    int?              BettingSecondsRemaining,
    List<NpcOddsDto>  NpcOdds,
    int               MapLength,
    DateTime?         RaceStartsAt    = null,
    DateTime?         BettingStartsAt = null);   // new
```

- [ ] **Step 6: Pass BettingStartsAt in GetCurrentSession**

In `GetCurrentSession.ExecuteAsync`, the final `return` line:

```csharp
return new CurrentSessionDto(
    session.Id,
    session.Status.ToString(),
    bettingSecondsRemaining,
    odds,
    session.MapLength,
    _store.RaceStartsAt,
    _store.BettingStartsAt);   // new
```

- [ ] **Step 7: Broadcast WaitingStarted in GameSessionManager**

In `RunSessionLifecycleAsync`, replace the current Waiting block:

```csharp
// Waiting phase
var bettingStartsAt = DateTime.UtcNow.AddSeconds(_settings.WaitingDurationSeconds);
_store.BettingStartsAt = bettingStartsAt;
await _hubNotifier.NotifyWaitingStartedAsync(session.Id, bettingStartsAt);
await Task.Delay(TimeSpan.FromSeconds(_settings.WaitingDurationSeconds), ct);
_store.BettingStartsAt = null;
```

- [ ] **Step 8: Build and test**

```bash
cd backend && dotnet build && dotnet test
```
Expected: 0 errors, all 24 tests pass.

- [ ] **Step 9: Commit**

```bash
git add backend/src/CubeRacing.Application/Interfaces/ICurrentSessionStore.cs \
        backend/src/CubeRacing.Application/Interfaces/IGameHubNotifier.cs \
        backend/src/CubeRacing.Application/Dtos/CurrentSessionDto.cs \
        backend/src/CubeRacing.Infrastructure/Services/CurrentSessionStore.cs \
        backend/src/CubeRacing.Infrastructure/Services/GameHubNotifier.cs \
        backend/src/CubeRacing.Infrastructure/Services/GameSessionManager.cs \
        backend/src/CubeRacing.Application/UseCases/GetCurrentSession.cs
git commit -m "feat: broadcast WaitingStarted event with bettingStartsAt timestamp"
```

---

### Task 3: Frontend — WaitingStartedMessage wiring

Add the new message type, wire SignalR parsing, register publisher/subscriber in VContainer, update `GameStateService`.

**Files:**
- Modify: `frontend/unity/Assets/Scripts/Core/Messages.cs`
- Modify: `frontend/unity/Assets/Scripts/Core/GameStateService.cs`
- Modify: `frontend/unity/Assets/Scripts/Core/Dtos.cs`
- Modify: `frontend/unity/Assets/Scripts/Network/SignalRClient.cs`
- Modify: `frontend/unity/Assets/Scripts/Main.cs`

**Interfaces:**
- Consumes: `"WaitingStarted"` SignalR event from Task 2
- Produces: `WaitingStartedMessage` (MessagePipe), `GameStateService.BettingStartsAt: ReactiveProperty<DateTime?>`

- [ ] **Step 1: Add WaitingStartedMessage to Messages.cs**

```csharp
public readonly struct WaitingStartedMessage
{
    public readonly DateTime BettingStartsAt;
    public WaitingStartedMessage(DateTime bettingStartsAt) => BettingStartsAt = bettingStartsAt;
}
```

- [ ] **Step 2: Add bettingStartsAt to CurrentSessionResponse in Dtos.cs**

```csharp
[Serializable]
public class CurrentSessionResponse
{
    public Guid sessionId;
    public string status;
    public int? bettingSecondsRemaining;
    public List<NpcOddsDto> npcOdds;
    public int mapLength;
    public DateTime? raceStartsAt;
    public DateTime? bettingStartsAt;   // new
}
```

- [ ] **Step 3: Add BettingStartsAt ReactiveProperty to GameStateService**

```csharp
public ReactiveProperty<DateTime?> BettingStartsAt { get; } = new(null);
```

- [ ] **Step 4: Add WaitingStartedMessage subscriber to GameStateService constructor and Initialize()**

Constructor parameter:
```csharp
ISubscriber<WaitingStartedMessage> waitingStartedSubscriber
```

In `Initialize()`:
```csharp
_waitingStartedSubscriber.Subscribe(m =>
    BettingStartsAt.Value = m.BettingStartsAt).AddTo(_bag);

_bettingStartedSubscriber.Subscribe(_ =>
{
    RaceStartsAt.Value   = null;
    BettingStartsAt.Value = null;   // add this line
    BetNpcId.Value       = null;
    BetAmount.Value      = null;
}).AddTo(_bag);
```

- [ ] **Step 5: Handle bettingStartsAt in GameStateService.ApplySession**

```csharp
public void ApplySession(CurrentSessionResponse session)
{
    CurrentSessionId      = session.sessionId;
    MapLength             = session.mapLength;
    SecondsRemaining.Value = session.bettingSecondsRemaining;
    NpcOdds.Value         = session.npcOdds ?? new();
    HasPlacedBet.Value    = false;
    WinnerNpcId.Value     = null;
    BettingStartsAt.Value = session.bettingStartsAt;   // new
    Status.Value          = session.status;
}
```

- [ ] **Step 6: Parse WaitingStarted in SignalRClient**

In the `switch` block inside `ReceiveLoopAsync`:
```csharp
case "WaitingStarted":
    var bettingStartsAt = args[0]["bettingStartsAt"].Value<DateTime>();
    _waitingStartedPublisher.Publish(new WaitingStartedMessage(bettingStartsAt));
    break;
```

Add `IPublisher<WaitingStartedMessage> _waitingStartedPublisher` field + constructor parameter.

- [ ] **Step 7: Register WaitingStartedMessage in Main.cs (VContainer)**

In `Main.cs`, add the message type to the MessagePipe registration block alongside existing message types:
```csharp
options.AddMessage<WaitingStartedMessage>();
```

Also register the publisher in `SignalRClient` construction: add `IPublisher<WaitingStartedMessage>` to the `SignalRClient` constructor call in the container.

- [ ] **Step 8: Verify Unity compiles with no errors**

Open Unity Editor (or use `mcp__UnityMCP__read_console` to check for compile errors after script edits).

- [ ] **Step 9: Commit**

```bash
git add frontend/unity/Assets/Scripts/Core/Messages.cs \
        frontend/unity/Assets/Scripts/Core/GameStateService.cs \
        frontend/unity/Assets/Scripts/Core/Dtos.cs \
        frontend/unity/Assets/Scripts/Network/SignalRClient.cs \
        frontend/unity/Assets/Scripts/Main.cs
git commit -m "feat: add WaitingStartedMessage and BettingStartsAt reactive property"
```

---

### Task 4: Frontend — Lobby countdowns

Add two countdowns to `LobbyPresenter`: waiting-phase countdown (「Next race in Ns...」) and race-entry countdown (「Welcome to watch the race! Will begin in Ns...」).

**Files:**
- Modify: `frontend/unity/Assets/Scripts/Lobby/LobbyPresenter.cs`

**Interfaces:**
- Consumes: `GameStateService.BettingStartsAt` (from Task 3), `GameStateService.RaceStartsAt` (already exists)

- [ ] **Step 1: Add a second CancellationTokenSource for race-entry countdown**

`LobbyPresenter` already has `_countdownCts` for betting countdown. Add:
```csharp
private CancellationTokenSource _raceEntryCts;
```

- [ ] **Step 2: Subscribe to BettingStartsAt in Start()**

```csharp
_gameState.BettingStartsAt
    .Where(t => t.HasValue && t.Value > DateTime.UtcNow)
    .Subscribe(t => StartWaitingCountdown(t!.Value))
    .AddTo(_disposables);
```

- [ ] **Step 3: Subscribe to RaceStartsAt in Start()**

```csharp
_gameState.RaceStartsAt
    .Subscribe(t =>
    {
        if (t.HasValue && t.Value > DateTime.UtcNow)
            StartRaceEntryCountdown(t.Value);
        else
            StopRaceEntryCountdown();
    })
    .AddTo(_disposables);
```

- [ ] **Step 4: Implement StartWaitingCountdown and its async loop**

```csharp
private void StartWaitingCountdown(DateTime bettingStartsAt)
{
    _countdownCts?.Cancel();
    _countdownCts = new CancellationTokenSource();
    WaitingCountdownAsync(bettingStartsAt, _countdownCts.Token).Forget();
}

private async UniTaskVoid WaitingCountdownAsync(DateTime bettingStartsAt, CancellationToken ct)
{
    while (!ct.IsCancellationRequested && DateTime.UtcNow < bettingStartsAt)
    {
        int remaining = Math.Max(0, (int)(bettingStartsAt - DateTime.UtcNow).TotalSeconds);
        _statusText.text = $"Next race in {remaining}s...";
        await UniTask.Delay(1000, cancellationToken: ct);
    }
}
```

- [ ] **Step 5: Implement StartRaceEntryCountdown / StopRaceEntryCountdown and async loop**

```csharp
private void StartRaceEntryCountdown(DateTime raceStartsAt)
{
    _raceEntryCts?.Cancel();
    _raceEntryCts?.Dispose();
    _raceEntryCts = new CancellationTokenSource();
    RaceEntryCountdownAsync(raceStartsAt, _raceEntryCts.Token).Forget();
}

private void StopRaceEntryCountdown()
{
    _raceEntryCts?.Cancel();
    _raceEntryCts?.Dispose();
    _raceEntryCts = null;
}

private async UniTaskVoid RaceEntryCountdownAsync(DateTime raceStartsAt, CancellationToken ct)
{
    while (!ct.IsCancellationRequested && DateTime.UtcNow < raceStartsAt)
    {
        int remaining = Math.Max(0, (int)(raceStartsAt - DateTime.UtcNow).TotalSeconds);
        _statusText.text = $"Welcome to watch the race! Will begin in {remaining}s...";
        await UniTask.Delay(1000, cancellationToken: ct);
    }
    if (!ct.IsCancellationRequested)
        _statusText.text = "Race in progress";
}
```

- [ ] **Step 6: Stop race-entry countdown on "Racing" → "Settling" status change**

In `OnStatusChanged`, add for `"Settling"` and `"Completed"` cases:
```csharp
private void OnStatusChanged(string status)
{
    // Stop race-entry countdown when race ends
    if (status is "Settling" or "Completed")
        StopRaceEntryCountdown();

    _statusText.text = status switch
    {
        "Waiting"   => "Preparing...",
        "Betting"   => $"Betting closes in {_gameState.SecondsRemaining.Value ?? 0}s",
        "Racing"    => "Race in progress",
        "Settling"  => "Settling...",
        "Completed" => $"{GetWinnerName()} wins!",
        _           => status
    };
    // ...rest unchanged
}
```

Note: The "Racing" text in `OnStatusChanged` is a fallback — if the race-entry countdown is running, it overrides `_statusText.text` each second. Once the countdown ends, `"Race in progress"` is written by the loop. If the player arrives after `RaceStartsAt` has passed, `OnStatusChanged("Racing")` shows "Race in progress" directly.

- [ ] **Step 7: Dispose _raceEntryCts in OnDestroy**

```csharp
private void OnDestroy()
{
    _disposables.Dispose();
    _countdownCts?.Cancel();
    _raceEntryCts?.Cancel();
    _raceEntryCts?.Dispose();
}
```

- [ ] **Step 8: Verify Unity compiles with no errors**

Check Unity console for compilation errors after saving.

- [ ] **Step 9: Commit**

```bash
git add frontend/unity/Assets/Scripts/Lobby/LobbyPresenter.cs
git commit -m "feat: add waiting-phase and race-entry countdowns in LobbyPresenter"
```
