# Race Scene Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix two bugs and add one feature in the Race Scene — stop animations after the winner lands, merge the winner banner into the settlement panel, and show round/NPC context in the header.

**Architecture:** All C# logic changes are in `RacePresenter.cs`. The only Inspector change is adding a `TMP_Text` child inside the existing `SettlementPanel` and removing the now-unused `WinnerBanner` GameObject.

**Tech Stack:** Unity 6, C#, DOTween, UniTask, R3, TextMeshPro, VContainer

## Global Constraints

- No backend changes, no new script files
- `_gameState.MapLength` (int, from `GameStateService`) is the authoritative finish square count; finish = index `MapLength - 1`
- Keep the serialized field named `_settlementPanel` (no rename to `_resultPanel`)
- DOTween: always call `DOKill()` before creating a new tween on the same target
- All tests/verification are Play Mode manual — there is no automated Unity UI test suite

---

### Task 1: Early Race End (`_raceOver` flag)

**Files:**
- Modify: `frontend/unity/Assets/Scripts/Race/RacePresenter.cs`

**Interfaces:**
- Produces: `private bool _raceOver` — used by `DrainQueueAsync` while-condition and `PlayRoundAsync` early return

- [ ] **Step 1: Add `_raceOver` field**

In `RacePresenter.cs`, add after line 56 (`private SettlementDonePayload _pendingSettlement;`):

```csharp
private bool _raceOver = false;
```

The fields block should now end:
```csharp
private int? _pendingWinnerNpcId;
private SettlementDonePayload _pendingSettlement;
private bool _raceOver = false;
```

- [ ] **Step 2: Add finish check inside `PlayRoundAsync` foreach loop**

At the end of the `foreach (var action in payload.actions)` loop body — after the `hadValidationError = true;` block (after line 301) and before the closing `}` — add:

```csharp
                if (action.toSquare == _gameState.MapLength - 1)
                {
                    _raceOver = true;
                    break;
                }
```

The end of the foreach body now reads:
```csharp
                if (!expectedSet.SetEquals(actualSet))
                {
                    Debug.LogError($"[Race] Stack mismatch NPC {action.npcId}. " +
                                   $"Expected: [{string.Join(",", expectedSet)}] " +
                                   $"Actual: [{string.Join(",", actualSet)}]");
                    hadValidationError = true;
                }

                if (action.toSquare == _gameState.MapLength - 1)
                {
                    _raceOver = true;
                    break;
                }
            }
```

- [ ] **Step 3: Add early return after `_bottomHud.Hide()` in `PlayRoundAsync`**

After the foreach loop closes, find `_bottomHud.Hide();` (line 304). Add `if (_raceOver) return;` immediately after it:

```csharp
            _bottomHud.Hide();

            if (_raceOver) return;

            _localStacks.Clear();
            foreach (var (k, v) in payload.squareStacks)
                if (int.TryParse(k, out int sq))
                    _localStacks[sq] = new List<int>(v);

            if (hadValidationError)
                ApplySquareStacks(payload.squareStacks);
```

When `_raceOver` is true we skip the squareStacks sync entirely — this is intentional (no snap on race end).

- [ ] **Step 4: Update `DrainQueueAsync` while condition**

On line 206, change:
```csharp
            while (_roundQueue.Count > 0 && !ct.IsCancellationRequested)
```
to:
```csharp
            while (_roundQueue.Count > 0 && !ct.IsCancellationRequested && !_raceOver)
```

- [ ] **Step 5: Verify in Play Mode**

Enter Play Mode. Observe the race until an NPC reaches the finish square. Verify:
1. That NPC's animation plays to the finish square fully
2. Remaining NPCs in the same round do **not** animate afterward
3. No errors in the Console
4. The game proceeds to the result panel (settlement still works — tested more fully in Task 2)

- [ ] **Step 6: Commit**

```bash
git add frontend/unity/Assets/Scripts/Race/RacePresenter.cs
git commit -m "feat: stop round animations immediately when NPC reaches finish"
```

---

### Task 2: Merged Result Panel

**Files:**
- Modify: `frontend/unity/Assets/Scripts/Race/RacePresenter.cs`
- Modify: `RaceScene.unity` (Unity Inspector — save from Editor after making changes)

**Interfaces:**
- Consumes: `_settlementPanel` (GameObject), `_settlementText` (TMP_Text), `_winnerText` (TMP_Text, re-wired in Inspector), `_returnButton` (Button), `_backButton` (Button)
- Produces: `ShowResult(int winnerNpcId, SettlementDonePayload settlement)`, `ApplySettlementText(SettlementDonePayload payload)`

Note: `SettlementDonePayload` is a reference type. Pass `null` to signal "settlement not yet received". If the project has `#nullable enable`, annotate the parameter as `SettlementDonePayload?`; otherwise leave it undecorated and rely on null checks.

- [ ] **Step 1: Remove `_winnerBanner` field, keep `_winnerText`**

Find the Winner banner block (lines 29–30):
```csharp
        // Winner banner
        [SerializeField] private GameObject _winnerBanner;
        [SerializeField] private TMP_Text   _winnerText;
```

Replace with:
```csharp
        // Winner text — child of _settlementPanel, wired in Inspector
        [SerializeField] private TMP_Text _winnerText;
```

- [ ] **Step 2: Update `Start()` — remove `_winnerBanner.SetActive(false)`**

In `Start()`, change:
```csharp
            _settlementPanel.SetActive(false);
            _winnerBanner.SetActive(false);
```
to:
```csharp
            _settlementPanel.SetActive(false);
```

- [ ] **Step 3: Update `_raceCompletedSubscriber` in `Start()`**

Replace the existing subscriber (lines 101–107):
```csharp
            _raceCompletedSubscriber.Subscribe(m =>
            {
                if (!_animating)
                    ShowWinnerAsync(m.WinnerNpcId, destroyCancellationToken).Forget();
                else
                    _pendingWinnerNpcId = m.WinnerNpcId;
            }).AddTo(_disposables);
```
with:
```csharp
            _raceCompletedSubscriber.Subscribe(m =>
            {
                if (!_animating)
                    ShowResult(m.WinnerNpcId, _pendingSettlement);
                else
                    _pendingWinnerNpcId = m.WinnerNpcId;
            }).AddTo(_disposables);
```

- [ ] **Step 4: Update `_settlementSubscriber` in `Start()`**

Replace the existing subscriber (lines 109–115):
```csharp
            _settlementSubscriber.Subscribe(m =>
            {
                if (!_animating)
                    ShowSettlement(m.Payload);
                else
                    _pendingSettlement = m.Payload;
            }).AddTo(_disposables);
```
with:
```csharp
            _settlementSubscriber.Subscribe(m =>
            {
                if (_animating)
                {
                    _pendingSettlement = m.Payload;
                }
                else if (_settlementPanel.activeSelf)
                {
                    // Panel already showing "結算中..." — fill in settlement text now
                    ApplySettlementText(m.Payload);
                }
                else
                {
                    _pendingSettlement = m.Payload;
                }
            }).AddTo(_disposables);
```

- [ ] **Step 5: Update `DrainQueueAsync` finally block**

Replace lines 213–225:
```csharp
            finally
            {
                _animating = false;
                if (_pendingWinnerNpcId.HasValue)
                {
                    ShowWinnerAsync(_pendingWinnerNpcId.Value, destroyCancellationToken).Forget();
                    _pendingWinnerNpcId = null;
                }
                if (_pendingSettlement != null)
                {
                    ShowSettlement(_pendingSettlement);
                    _pendingSettlement = null;
                }
            }
```
with:
```csharp
            finally
            {
                _animating = false;
                if (_pendingWinnerNpcId.HasValue)
                {
                    ShowResult(_pendingWinnerNpcId.Value, _pendingSettlement);
                    _pendingWinnerNpcId = null;
                    _pendingSettlement   = null;
                }
            }
```

- [ ] **Step 6: Remove `ShowWinnerAsync` and `ShowSettlement`; add `ShowResult` and `ApplySettlementText`**

Remove the entire `ShowWinnerAsync` method (lines 333–344):
```csharp
        private async UniTaskVoid ShowWinnerAsync(int winnerNpcId, CancellationToken ct)
        {
            var entry = _npcConfig.GetById(winnerNpcId);
            _winnerText.text = $"{entry?.npcName ?? winnerNpcId.ToString()} wins!";
            _winnerBanner.SetActive(true);

            if (_board.NpcCubes.TryGetValue(winnerNpcId, out var cube))
                cube.transform.DOPunchScale(Vector3.one * 0.5f, 0.6f, 5);

            await UniTask.Delay(1500, cancellationToken: ct);
        }
```

Remove the entire `ShowSettlement` method (lines 346–363):
```csharp
        private void ShowSettlement(SettlementDonePayload payload)
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

            _settlementPanel.SetActive(true);
            _backButton.interactable = true;
        }
```

Add in their place (before `ReturnToLobby`):
```csharp
        private void ShowResult(int winnerNpcId, SettlementDonePayload settlement)
        {
            var entry = _npcConfig.GetById(winnerNpcId);
            _winnerText.text = $"{entry?.npcName ?? winnerNpcId.ToString()} wins!";

            if (_board.NpcCubes.TryGetValue(winnerNpcId, out var cube))
                cube.transform.DOPunchScale(Vector3.one * 0.5f, 0.6f, 5);

            if (settlement != null)
                ApplySettlementText(settlement);
            else
                _settlementText.text = "結算中...";

            _settlementPanel.SetActive(true);
            _backButton.interactable = true;
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

- [ ] **Step 7: Compile check**

Open Unity Editor. Wait for compilation. Check Console for errors — there should be none. If you see "The name `_winnerBanner` does not exist" — you missed removing a reference; search the file for `_winnerBanner` and remove it.

- [ ] **Step 8: Unity Inspector — add `_winnerText` TMP_Text inside SettlementPanel**

In the **Hierarchy** of `RaceScene`:
1. Find the `SettlementPanel` GameObject (child of the Canvas, previously controlled by `_settlementPanel`)
2. Right-click `SettlementPanel` → **UI → Text - TextMeshPro** — name the new object `WinnerText`
3. Drag `WinnerText` to be the **first child** of `SettlementPanel` (above the existing settlement text and return button) — this controls visual order
4. Style the `WinnerText`: Bold, large font, centered, color white or gold — it should be visually prominent

In the **Inspector** for the `RacePresenter` component:
5. Find the `Winner Text` serialized field — it is currently missing (was wired to the old WinnerBanner's text child). Wire it to the new `WinnerText` TMP_Text created in step 2
6. The `Winner Banner` serialized field should now be **gone** from the Inspector (removed in Step 1) — if it still shows a missing reference, the code change from Step 1 has not compiled yet; wait and try again

Remove the old `WinnerBanner`:
7. Find the `WinnerBanner` GameObject in the Hierarchy (it is now unreferenced). Delete it or deactivate it permanently.

Save the scene: **File → Save** (Cmd+S / Ctrl+S)

- [ ] **Step 9: Verify in Play Mode**

Enter Play Mode. Watch a race to completion. Verify:
1. **No overlap**: only one panel appears when the race ends
2. **Winner name** shows at the top of the panel (e.g., "Cube A wins!")
3. **Settlement text** shows either "結算中..." immediately then updates, or the final result directly
4. **Chips counter** updates correctly after settlement
5. **Return button** is clickable and navigates back to Lobby

- [ ] **Step 10: Commit**

```bash
git add frontend/unity/Assets/Scripts/Race/RacePresenter.cs
git add frontend/unity/Assets/Scenes/RaceScene.unity
git commit -m "feat: merge WinnerBanner into SettlementPanel as single result panel"
```

---

### Task 3: Header Status Text

**Files:**
- Modify: `frontend/unity/Assets/Scripts/Race/RacePresenter.cs`

**Interfaces:**
- Consumes: `payload.roundNumber` (int), `action.npcId` (int), `_npcConfig.GetById(int)?.npcName` (string?)
- Produces: none — side-effect only (`_statusText.text`)

- [ ] **Step 1: Fix `StatusToText` — suppress "Race over" and "Settling..." mid-animation**

Replace lines 368–374:
```csharp
        private static string StatusToText(string status) => status switch
        {
            "Racing"    => "Race in progress",
            "Settling"  => "Settling...",
            "Completed" => "Race over",
            _           => status
        };
```
with:
```csharp
        private static string StatusToText(string status) => status switch
        {
            "Racing"    => "Race in progress",
            "Settling"  => "",
            "Completed" => "",
            _           => status
        };
```

`PlayRoundAsync` now owns the header text for the duration of the race. After all animations finish and the result panel appears, the header text is irrelevant (covered by the panel).

- [ ] **Step 2: Set "Round N" header at start of `PlayRoundAsync`**

In `PlayRoundAsync`, find line 247 (`await _roundToast.ShowAsync(payload.roundNumber, ct);`). Add the header assignment **before** it:

```csharp
            _statusText.text = $"Round {payload.roundNumber}";
            await _roundToast.ShowAsync(payload.roundNumber, ct);
```

- [ ] **Step 3: Set "Round N - {NpcName} run!" header before each NPC animates**

Inside the `foreach (var action in payload.actions)` loop, find line 260 (`_bottomHud.SetActiveNpc(action.npcId);`). Add the header assignment and NPC name lookup immediately **before** it — after the two `continue` guards at lines 255–258, so it only runs for NPCs that will actually animate:

```csharp
                var npcName = _npcConfig.GetById(action.npcId)?.npcName ?? $"NPC {action.npcId}";
                _statusText.text = $"Round {payload.roundNumber} - {npcName} run!";
                _bottomHud.SetActiveNpc(action.npcId);
```

- [ ] **Step 4: Verify in Play Mode**

Enter Play Mode. Watch a full race. Verify:
1. Header shows `"Round 1"` at the start of round 1, then `"Round 1 - Cube A run!"` before Cube A moves, etc.
2. **"Race over" does not appear mid-animation** — this was the original bug
3. During the Betting phase, header still shows normal status text (e.g., the countdown or "Betting")
4. After the result panel appears, the header text is either empty or the last NPC text — doesn't matter, the panel dominates

- [ ] **Step 5: Commit**

```bash
git add frontend/unity/Assets/Scripts/Race/RacePresenter.cs
git commit -m "fix: show Round N and NPC name in header during race; suppress premature 'Race over'"
```
