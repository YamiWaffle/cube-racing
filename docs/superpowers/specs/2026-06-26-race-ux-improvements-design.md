# Race UX Improvements — Design Spec

**Date:** 2026-06-26  
**Scope:** Three independent UX improvements to the race flow

---

## Feature 1: Race Start Delay + Countdown

### Goal

Prevent late-join NPCs-not-moving issue by introducing a 30-second buffer between betting end and round 1. Players who enter the race scene during this buffer see a countdown instead of a frozen screen.

### Backend Changes

**`GameSettings`**
- Add `RaceStartDelaySeconds: int = 30`

**`IGameHubNotifier`**
- Add `NotifyRaceStartingAsync(Guid sessionId, DateTime raceStartsAt)`

**`GameHubNotifier`**
- Implement: send `"RaceStarting"` with `{ sessionId, raceStartsAt }` to `Clients.Group(sessionId.ToString())`

**`ICurrentSessionStore`**
- Add `RaceStartsAt: DateTime?` property
- Set by `BettingEndedConsumer` after computing the start time
- Cleared by `GameSessionManager` at the start of each new session loop (before creating the next `GameSession`)

**`BettingEndedConsumer.HandleAsync()`** — revised flow:
```
session.StartRacing()
await sessionRepo.UpdateAsync(session)
await hubNotifier.NotifyBettingEndedAsync(sessionId)

raceStartsAt = DateTime.UtcNow.AddSeconds(settings.RaceStartDelaySeconds)
_store.RaceStartsAt = raceStartsAt
await hubNotifier.NotifyRaceStartingAsync(sessionId, raceStartsAt)

await Task.Delay(settings.RaceStartDelaySeconds * 1000, ct)

// --- existing race loop begins here ---
while (simulator.GetWinner() is null) { ... }
```

**`CurrentSessionResponse`**
- Add `raceStartsAt: DateTime?` field, populated from `ICurrentSessionStore.RaceStartsAt`
- Used by late joiners who missed the `"RaceStarting"` SignalR event

### Frontend Changes

**`Messages.cs`**
- Add `RaceStartingMessage { DateTime RaceStartsAt }`

**`SignalRClient`**
- Handle `"RaceStarting"` case: deserialize `args[0]["raceStartsAt"]` → publish `RaceStartingMessage`

**`GameStateService`**
- Add `ReactiveProperty<DateTime?> RaceStartsAt`
- Subscribe to `RaceStartingMessage` → set `RaceStartsAt.Value`
- Clear `RaceStartsAt.Value = null` on `BettingStartedMessage`

**`RacePresenter.Start()`**
- Subscribe to `GameStateService.RaceStartsAt`:
  - If value is not null and `> DateTime.UtcNow` → call `StartCountdown(raceStartsAt)`

**`RacePresenter.SyncNpcPositionsAsync()`** (existing method)
- After fetching session, also check `session.raceStartsAt`:
  - If `raceStartsAt > UtcNow` and `GameStateService.RaceStartsAt` is null (missed the SignalR event) → set `GameStateService.RaceStartsAt.Value = session.raceStartsAt`

**`RacePresenter.CountdownAsync(DateTime raceStartsAt, CancellationToken ct)`**
```
while DateTime.UtcNow < raceStartsAt && !ct.IsCancellationRequested:
    remaining = (int)(raceStartsAt - DateTime.UtcNow).TotalSeconds
    _statusText.text = $"The racing will begin in {remaining} seconds...."
    await UniTask.Delay(1000, ct)
// countdown complete — status text reverts to normal status subscription
```

---

## Feature 2: Step-by-Step Animation + Parent-Child Hierarchy

### Goal

Replace direct-to-`toSquare` jumping with one-square-at-a-time movement. Use Unity's transform parent-child hierarchy so stacked NPCs move together automatically, without the mover needing to manage carried NPCs explicitly.

### Stacking Rule

> **A is on top of B → A is B's GameObject child.**  
> When B moves, A follows automatically via Unity transform hierarchy.

### State Added to `RacePresenter`

```csharp
private readonly Dictionary<int, List<int>> _localStacks = new();
// key: square index, value: NPC IDs bottom-to-top
```

### `ApplySquareStacks(Dictionary<string, List<int>> stacks)` — extended

In addition to teleporting positions, build the parent-child chain:

```
foreach (square, [npcA, npcB, npcC]) in stacks:   // A=bottom, C=top
    npcA.SetParent(null)
    npcB.SetParent(npcA)    // localPos = up * 0.5
    npcC.SetParent(npcB)    // localPos = up * 0.5

Also populate _localStacks from stacks.
```

### `NpcCubeController`

- Add `public int NpcId { get; private set; }` (set in `Initialize`)
- Needed by the descendant-collection helper

### `PlayRoundAsync` — revised action processing

For each `action` in `payload.actions`:

```
steps = action.toSquare - action.fromSquare

1. Remove action.npcId (and carriedNpcIds) from _localStacks[action.fromSquare]
2. movingCube.SetParent(null)              // de-parent, world pos preserved
   (carriedNpcIds remain as movingCube's descendants)

3. stepDuration = durationPerAction / steps

4. for sq = fromSquare+1 to toSquare:
     stackCount  = _localStacks.TryGetValue(sq, out var list) ? list.Count : 0
     targetWorld = GetSquarePosition(sq) + Vector3.up * 0.5f * stackCount

     await movingCube.MoveToAsync(targetWorld, stepDuration)

     if stackCount > 0:
         topNpc = NpcCubes[_localStacks[sq].Last()]
         movingCube.SetParent(topNpc)      // worldPositionStays = true (default)
     else:
         movingCube.SetParent(null)

     if sq != toSquare:
         movingCube.SetParent(null)        // only movingCube de-parented; its children (carriedNpcIds) remain attached and move with it; topNpc stays at sq

5. // Arrived at toSquare. Parent already set (or null) from step 4 final iteration.

6. _localStacks[toSquare] = existing + [action.npcId] + action.carriedNpcIds

7. Validate:
     actual   = GetAllDescendantIds(action.npcId)   // all children + grandchildren
     expected = new HashSet<int>(action.carriedNpcIds)
     if actual.ToHashSet() != expected:
         Debug.LogError($"[Race] Stack mismatch NPC {action.npcId}. " +
                        $"Expected carried: [{Join(expected)}] " +
                        $"Actual descendants: [{Join(actual)}]")
```

After all actions in a round complete:
- Always reset `_localStacks` from `payload.squareStacks` (authoritative re-sync before next round)
- If any validation error was logged: also call `ApplySquareStacks(payload.squareStacks)` to teleport NPCs to correct positions and rebuild the parent-child hierarchy (safe to do since all animations for the round are already awaited and complete)

### Helper: `GetAllDescendantIds(int npcId)`

```csharp
private List<int> GetAllDescendantIds(int npcId)
{
    var result = new List<int>();
    Collect(NpcCubes[npcId].transform, result);
    return result;
}

private static void Collect(Transform t, List<int> result)
{
    foreach (Transform child in t)
    {
        var ctrl = child.GetComponent<NpcCubeController>();
        if (ctrl != null) result.Add(ctrl.NpcId);
        Collect(child, result);
    }
}
```

### Edge Cases

- **steps = 0**: should not occur (dice roll ≥ 1), but guard with early return if `toSquare == fromSquare`
- **De-parent world pos drift**: `SetParent(null)` preserves world position by default in Unity — no explicit save/restore needed
- **_localStacks missing key**: use `TryGetValue` with fallback to empty list / count 0

---

## Feature 3: Lobby Bet Indicator

### Goal

After placing a bet, highlight the chosen NPC card and grey out all others so the player can clearly see their selection.

### `GameStateService`

- Add `ReactiveProperty<int?> BetNpcId`
- Add `ReactiveProperty<int?> BetAmount`
- Add method `SetBet(int npcId, int amount)` — sets both properties
- Clear both to `null` on `BettingStartedMessage` (new session)

### `BettingDialogPresenter`

- After successful `PlaceBetAsync`, call `_gameState.SetBet(npcId, amount)`

### `NpcCardView`

Add two new visual methods:

**`SetBetHighlight(int amount)`**
- Apply gold/yellow border (e.g., Image color on a border component)
- Show a badge `TMP_Text` with text `✓ Bet {amount:N0}`

**`SetGreyedOut(bool greyed)`**
- Toggle a semi-transparent grey `Image` overlay panel (alpha ≈ 0.55)
- If `greyed = false`: hide overlay

Both methods are additive to the existing `SetBettingEnabled` logic — they apply on top of it.

### `LobbyPresenter`

Subscribe to `GameStateService.BetNpcId` in `Start()`:

```csharp
_gameState.BetNpcId.Subscribe(npcId =>
{
    // Reset all cards first
    foreach (var card in _cards.Values)
    {
        card.SetGreyedOut(false);
        // (badge hidden implicitly when not highlighting)
    }

    if (npcId.HasValue && _cards.TryGetValue(npcId.Value, out var betCard))
    {
        betCard.SetBetHighlight(_gameState.BetAmount.CurrentValue ?? 0);
        foreach (var (id, card) in _cards)
            if (id != npcId.Value) card.SetGreyedOut(true);
    }
}).AddTo(_disposables);
```

Reset happens automatically when `BetNpcId` becomes `null` on new session.

---

## Summary of Files Changed

| File | Change |
|------|--------|
| `GameSettings.cs` | + `RaceStartDelaySeconds` |
| `ICurrentSessionStore.cs` | + `RaceStartsAt` |
| `IGameHubNotifier.cs` | + `NotifyRaceStartingAsync` |
| `GameHubNotifier.cs` | implement `NotifyRaceStartingAsync` |
| `BettingEndedConsumer.cs` | add delay + `NotifyRaceStartingAsync` call |
| `GetCurrentSession.cs` (or response DTO) | + `raceStartsAt` field |
| `Messages.cs` | + `RaceStartingMessage` |
| `SignalRClient.cs` | handle `"RaceStarting"` |
| `GameStateService.cs` | + `RaceStartsAt`, `BetNpcId`, `BetAmount`, `SetBet()` |
| `RacePresenter.cs` | countdown logic, `_localStacks`, step-by-step animation, validation |
| `NpcCubeController.cs` | + `NpcId` property |
| `NpcCardView.cs` | + `SetBetHighlight()`, `SetGreyedOut()` |
| `BettingDialogPresenter.cs` | call `_gameState.SetBet()` after success |
| `LobbyPresenter.cs` | subscribe to `BetNpcId`, update card visuals |
