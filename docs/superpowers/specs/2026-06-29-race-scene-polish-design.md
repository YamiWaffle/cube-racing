# Race Scene Polish — Implementation Design

## Goal

Fix three overlapping issues in the Race Scene:
1. WinnerBanner and SettlementPanel display simultaneously and overlap — merge into one result panel.
2. Header status text shows "Race over" mid-animation (bug) and lacks round/NPC context during races (feature).
3. Race does not end visually when a NPC reaches the finish line — it waits for remaining NPCs in the same round to animate (bug).

## Architecture

All changes are contained in `RacePresenter.cs` (C# logic) and the `RaceScene.unity` Inspector (add `_winnerText` TMP_Text to the existing settlement panel, wire `_resultPanel`). No backend changes required.

---

## Section 1 — Early Race End (Item 3)

### Root Cause

`PlayRoundAsync` iterates all `payload.actions` to completion before returning, so remaining NPCs animate even after the winner's action has landed on the finish square.

### Design

**New field:**
```csharp
private bool _raceOver = false;
```

**In `PlayRoundAsync`, after each NPC's step-by-step animation loop completes:**
```csharp
if (action.toSquare == _gameState.MapLength - 1)
{
    _bottomHud.Hide();
    _raceOver = true;
    break;
}
```
No snap to `payload.squareStacks`. Just break.

**In `DrainQueueAsync` while condition:**
```csharp
while (_roundQueue.Count > 0 && !ct.IsCancellationRequested && !_raceOver)
```
Remaining queued rounds (theoretically none after the winning round, but defensive) are silently discarded.

`_raceOver` is an instance field on a MonoBehaviour; it resets to `false` automatically when the scene is reloaded for the next session.

---

## Section 2 — Merged Result Panel (Item 1)

### Root Cause

`DrainQueueAsync` finally block fires both `ShowWinnerAsync` (async, `.Forget()`) and `ShowSettlement` (sync) in the same frame. Both GameObjects become active simultaneously.

### Design

**Remove:**
- `[SerializeField] private GameObject _winnerBanner;`
- `[SerializeField] private TMP_Text _winnerText;` (old WinnerBanner reference; a new one is added inside the settlement panel)
- `ShowWinnerAsync` method

**Keep (still used):**
- `private int? _pendingWinnerNpcId;` — set by `_raceCompletedSubscriber` when animating, consumed in finally block

**Unity Inspector change:**
- Add a `TMP_Text _winnerText` as the top child of the existing `_settlementPanel` GameObject. Rename the serialised field reference from `_settlementPanel` to `_resultPanel` for clarity (optional; the GameObject itself stays the same).
- Layout inside panel: `_winnerText` (top) → `_settlementText` (bottom) → `_returnButton`.

**New method replacing both `ShowWinnerAsync` and `ShowSettlement`:**
```csharp
private void ShowResult(int winnerNpcId, SettlementDonePayload? settlement)
{
    var entry = _npcConfig.GetById(winnerNpcId);
    _winnerText.text = $"{entry?.npcName ?? winnerNpcId.ToString()} wins!";

    if (_board.NpcCubes.TryGetValue(winnerNpcId, out var cube))
        cube.transform.DOPunchScale(Vector3.one * 0.5f, 0.6f, 5);

    if (settlement != null)
        ApplySettlementText(settlement);
    else
        _settlementText.text = "結算中...";

    _resultPanel.SetActive(true);
}

private void ApplySettlementText(SettlementDonePayload payload)
{
    var me = payload.playerResults?.Find(r => r.nickname == _session.Nickname);
    if (me != null)
    {
        _session.UpdateChips(_session.Chips.CurrentValue + me.winAmount);
        _settlementText.text = me.winAmount > 0
            ? $"You won +{me.winAmount:N0} chips!"
            : "No win this round";
    }
    else
    {
        _settlementText.text = "No bet this round";
    }
}
```

**Updated `DrainQueueAsync` finally block:**
```csharp
finally
{
    _animating = false;
    if (_pendingWinnerNpcId.HasValue)          // keep field, still used
    {
        ShowResult(_pendingWinnerNpcId.Value, _pendingSettlement);
        _pendingWinnerNpcId = null;
        _pendingSettlement   = null;
    }
    else if (_pendingSettlement != null)       // settlement arrived but no winner yet (edge case)
    {
        // do nothing here; winner not yet known
    }
}
```

**Updated `_settlementSubscriber` handler:**
```csharp
_settlementSubscriber.Subscribe(m =>
{
    if (_animating)
    {
        _pendingSettlement = m.Payload;
    }
    else if (_resultPanel.activeSelf)
    {
        // Result panel already showing "結算中..." — fill in the settlement text now
        ApplySettlementText(m.Payload);
    }
    // else: settlement arrived before winner (unexpected); store it
    else
    {
        _pendingSettlement = m.Payload;
    }
}).AddTo(_disposables);
```

**Updated `_raceCompletedSubscriber` handler:**
```csharp
_raceCompletedSubscriber.Subscribe(m =>
{
    if (!_animating)
        ShowResult(m.WinnerNpcId, _pendingSettlement);
    else
        _pendingWinnerNpcId = m.WinnerNpcId;
}).AddTo(_disposables);
```

**Initialisation in `Start()`:** replace `_winnerBanner.SetActive(false)` with `_resultPanel.SetActive(false)`.

---

## Section 3 — Header Status Text (Items 2.1 & 2.2)

### Root Cause (2.1)

`GameStateService.Status` is updated reactively. `Status = "Completed"` fires when `SettlementDone` arrives, potentially mid-animation. `StatusToText("Completed")` → `"Race over"` immediately overwrites the header.

### Design

**Fix 2.1 — `StatusToText`:**
```csharp
private static string StatusToText(string status) => status switch
{
    "Racing"    => "Race in progress",
    "Settling"  => "",        // managed by PlayRoundAsync
    "Completed" => "",        // managed by ShowResult
    _           => status
};
```
"Settling" and "Completed" return empty string. The subscription no longer shows race-ending text mid-animation.

**Feature 2.2 — Round/NPC text in `PlayRoundAsync`:**

At the top of `PlayRoundAsync` (before `_roundToast.ShowAsync`):
```csharp
_statusText.text = $"Round {payload.roundNumber}";
```

Inside the action foreach loop, before `await _camera.FocusOnAsync(...)`:
```csharp
var npcName = _npcConfig.GetById(action.npcId)?.npcName ?? $"NPC {action.npcId}";
_statusText.text = $"Round {payload.roundNumber} - {npcName} run!";
```

After the race ends, `_statusText` keeps its last value (the final NPC's text). The result panel dominates the visual focus so the header text is irrelevant at that point.

---

## Files Changed

| File | Change |
|---|---|
| `Assets/Scripts/Race/RacePresenter.cs` | All logic changes (Items 1, 2.1, 2.2, 3) |
| `Assets/Scenes/RaceScene.unity` | Add `_winnerText` TMP_Text to settlement panel; update Inspector wiring |

No backend changes. No new script files.
