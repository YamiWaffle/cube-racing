# Race Presentation UX — Design Spec

**Date:** 2026-06-28
**Scope:** Three improvements to the race scene viewing experience, plus NPC height configuration

---

## Overview

Four features in one spec:

1. **Round toast** — brief "Round N" popup before each round's dice display
2. **Dice roll panel** — shows all 4 NPCs' dice rolls simultaneously with a number-rolling animation, then hides before movement begins
3. **Bottom HUD** — persistent during movement; shows each NPC's steps for the round, highlights the currently moving NPC and greys out others
4. **Camera follow** — between NPC actions: pause → quick lerp to next NPC → smooth follow while it moves
5. **NPC height config** — `npcHeight` field in `RaceConfig` to replace hardcoded `0.5f` stacking offsets

Features 1–4 are all in the race scene and touch `PlayRoundAsync`. Feature 5 is a small independent change to `RaceConfig` and `BoardController`.

> **Note:** Winner banner + settlement panel sequencing (show after all animations) was already implemented in commit `4781d20` and requires no changes here.

---

## Global Constraints

- Tech stack: Unity 6, VContainer, UniTask, DOTween, R3, MessagePipe
- All new MonoBehaviours follow the existing component pattern: serialized fields for Inspector wiring, no runtime `Find` or `GetComponent` in hot paths
- All async methods accept `CancellationToken ct` and propagate it
- `DiceSlotView` and `RoundHudSlotView` are thin view components with no business logic
- `RaceCameraController` is registered in `RaceScope` via `RegisterComponentInHierarchy`
- `npcHeight` default value: `0.5f` (matches current hardcoded value — no visual change on day one)
- Do NOT add fields beyond those listed (YAGNI)
- Durations are `[SerializeField] float` fields so they can be tweaked in Inspector without recompiling

---

## Files

| Action | Path |
|--------|------|
| Create | `frontend/unity/Assets/Scripts/Race/RoundToastView.cs` |
| Create | `frontend/unity/Assets/Scripts/Race/DiceSlotView.cs` |
| Create | `frontend/unity/Assets/Scripts/Race/DiceRollPanelView.cs` |
| Create | `frontend/unity/Assets/Scripts/Race/RoundHudSlotView.cs` |
| Create | `frontend/unity/Assets/Scripts/Race/RoundBottomHud.cs` |
| Create | `frontend/unity/Assets/Scripts/Race/RaceCameraController.cs` |
| Modify | `frontend/unity/Assets/Scripts/Race/RacePresenter.cs` |
| Modify | `frontend/unity/Assets/Scripts/Race/RaceScope.cs` |
| Modify | `frontend/unity/Assets/Scripts/Config/RaceConfig.cs` |
| Modify | `frontend/unity/Assets/Scripts/Race/BoardController.cs` |
| Unity Editor | Create prefabs/UI for RoundToastView, DiceRollPanelView, RoundBottomHud; wire to RacePresenter Inspector |

---

## Feature 5: NPC Height Config (simplest — do first)

### `RaceConfig.cs`

Add one field after `stepDuration`:

```csharp
[Tooltip("Height of each NPC cube (metres). Used for stacking offset calculations.")]
public float npcHeight = 0.5f;
```

### `BoardController.cs`

Inject `RaceConfig` the same way as `NpcConfig`:

```csharp
private RaceConfig _raceConfig;

[Inject]
public void Construct(NpcConfig npcConfig, RaceConfig raceConfig)
{
    _npcConfig  = npcConfig;
    _raceConfig = raceConfig;
}
```

In `SpawnNpcCubes`, replace hardcoded `0.5f` stacking offsets:

```csharp
// Before:
Vector3 offset = new Vector3(i * 0.25f - (count - 1) * 0.125f, i * 0.5f + 0.5f, 0f);

// After:
float h = _raceConfig.npcHeight;
Vector3 offset = new Vector3(i * (h * 0.5f) - (count - 1) * (h * 0.25f), i * h + h, 0f);
```

### `RacePresenter.cs` — replace all `0.5f` stacking constants with `_raceConfig.npcHeight`

In `PlayRoundAsync` — target height calculation:
```csharp
// Before:
var targetWorld = _board.GetSquarePosition(sq) + Vector3.up * (0.5f + stackCount * 0.5f);

// After:
float h = _raceConfig.npcHeight;
var targetWorld = _board.GetSquarePosition(sq) + Vector3.up * (h + stackCount * h);
```

In `ApplySquareStacks` — bottom NPC base position and child local offset:
```csharp
// Before:
cube.transform.position = sqPos + Vector3.up * 0.5f;
// ...
cube.transform.localPosition = Vector3.up * 0.5f;

// After:
cube.transform.position = sqPos + Vector3.up * _raceConfig.npcHeight;
// ...
cube.transform.localPosition = Vector3.up * _raceConfig.npcHeight;
```

---

## Feature 1–4: Race Presentation UX

### Execution sequence in `PlayRoundAsync`

```
OLD: foreach action → animate NPC (no inter-round rhythm)

NEW:
// Build lookup tables first (see "Steps calculation" section below)
var npcSteps = ...
var npcDice  = ...

1. await _roundToast.ShowAsync(payload.roundNumber, ct)
2. await _dicePanel.ShowAsync(payload.actions, _npcConfig, npcDice, ct)
3. _bottomHud.SetRound(npcSteps)

4. foreach action in payload.actions:
   a. _bottomHud.SetActiveNpc(action.npcId)
   b. await _camera.FocusOnAsync(movingCube.transform, ct)
   c. [existing per-step DOJump animation — unchanged]

5. _bottomHud.Hide()
```

Steps 1–3 run before any NPC moves. Step 5 runs after the last NPC in this round finishes.

### Steps calculation for HUD and dice panel

Build a `Dictionary<int, int> npcSteps` before step 3:

```csharp
var npcSteps = new Dictionary<int, int>();
foreach (var action in payload.actions)
{
    int steps = action.toSquare - action.fromSquare;
    npcSteps[action.npcId] = steps;
    foreach (var carried in action.carriedNpcIds)
        npcSteps[carried] = steps;   // carried NPCs move same distance as leader
}
// NPCs absent from npcSteps moved 0 squares this round (show "—")
```

Similarly, build `Dictionary<int, int> npcDice`:
```csharp
var npcDice = new Dictionary<int, int>();
foreach (var action in payload.actions)
{
    npcDice[action.npcId] = action.diceRoll;
    foreach (var carried in action.carriedNpcIds)
        npcDice[carried] = action.diceRoll;
}
```

---

## New Components

### `RoundToastView.cs`

```csharp
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using System.Threading;

namespace CubeRacing
{
    public class RoundToastView : MonoBehaviour
    {
        [SerializeField] private TMP_Text _text;
        [SerializeField] private float    _displayDuration = 0.8f;

        public async UniTask ShowAsync(int round, CancellationToken ct)
        {
            _text.text = $"Round {round}";
            gameObject.SetActive(true);
            await UniTask.Delay((int)(_displayDuration * 1000), cancellationToken: ct);
            gameObject.SetActive(false);
        }
    }
}
```

---

### `DiceSlotView.cs`

Displays one NPC's dice roll with a rolling number animation.

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CubeRacing
{
    public class DiceSlotView : MonoBehaviour
    {
        [SerializeField] private Image    _colorBlock;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _numberText;
        [SerializeField] private float    _rollIntervalSec = 0.08f;

        public void Setup(NpcEntry entry)
        {
            _colorBlock.color = entry.color;
            _nameText.text    = entry.npcName;
            _numberText.text  = "—";
        }

        public void SetEmpty()
        {
            _numberText.text = "—";
        }

        // Rolls the number display for rollDuration seconds, then snaps to finalValue.
        public async UniTask RollAsync(int finalValue, float rollDuration, CancellationToken ct)
        {
            float elapsed = 0f;
            while (elapsed < rollDuration && !ct.IsCancellationRequested)
            {
                _numberText.text = Random.Range(1, 4).ToString();
                await UniTask.Delay((int)(_rollIntervalSec * 1000), cancellationToken: ct);
                elapsed += _rollIntervalSec;
            }
            _numberText.text = finalValue.ToString();
        }
    }
}
```

---

### `DiceRollPanelView.cs`

Owns 4 `DiceSlotView` children. Coordinates simultaneous rolling, then hides.

```csharp
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CubeRacing
{
    public class DiceRollPanelView : MonoBehaviour
    {
        [SerializeField] private DiceSlotView[] _slots;          // length must match NpcConfig.npcs
        [SerializeField] private float          _rollDuration = 1.2f;
        [SerializeField] private float          _holdDuration = 0.5f;

        public async UniTask ShowAsync(
            List<RoundActionDto> actions,
            NpcConfig npcConfig,
            Dictionary<int, int> npcDice,
            CancellationToken ct)
        {
            gameObject.SetActive(true);

            // Setup slots
            var tasks = new UniTask[_slots.Length];
            for (int i = 0; i < _slots.Length; i++)
            {
                var entry = npcConfig.npcs[i];
                _slots[i].Setup(entry);
                if (npcDice.TryGetValue(entry.id, out int dice))
                    tasks[i] = _slots[i].RollAsync(dice, _rollDuration, ct);
                else
                {
                    _slots[i].SetEmpty();
                    tasks[i] = UniTask.CompletedTask;
                }
            }

            await UniTask.WhenAll(tasks);
            await UniTask.Delay((int)(_holdDuration * 1000), cancellationToken: ct);
            gameObject.SetActive(false);
        }
    }
}
```

---

### `RoundHudSlotView.cs`

One slot in the bottom HUD — shows NPC color, name, step count, and a grey overlay for the inactive state.

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CubeRacing
{
    public class RoundHudSlotView : MonoBehaviour
    {
        [SerializeField] private Image    _colorBlock;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _stepsText;
        [SerializeField] private Image    _greyOverlay;   // semi-transparent, toggled

        public void Setup(NpcEntry entry, int? steps)
        {
            _colorBlock.color = entry.color;
            _nameText.text    = entry.npcName;
            _stepsText.text   = steps.HasValue ? steps.Value.ToString() : "—";
        }

        public void SetActive(bool active)
        {
            _greyOverlay.gameObject.SetActive(!active);
        }
    }
}
```

---

### `RoundBottomHud.cs`

Manages 4 `RoundHudSlotView` children. Provides `SetRound`, `SetActiveNpc`, `Hide`.

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace CubeRacing
{
    public class RoundBottomHud : MonoBehaviour
    {
        [SerializeField] private RoundHudSlotView[] _slots;   // one per NPC, order matches NpcConfig.npcs

        private NpcConfig _npcConfig;

        public void Initialize(NpcConfig npcConfig)
        {
            _npcConfig = npcConfig;
        }

        public void SetRound(Dictionary<int, int> npcSteps)
        {
            gameObject.SetActive(true);
            for (int i = 0; i < _slots.Length; i++)
            {
                var entry = _npcConfig.npcs[i];
                int? steps = npcSteps.TryGetValue(entry.id, out int s) ? s : null;
                _slots[i].Setup(entry, steps);
                _slots[i].SetActive(false);   // all greyed until SetActiveNpc
            }
        }

        public void SetActiveNpc(int npcId)
        {
            for (int i = 0; i < _slots.Length; i++)
                _slots[i].SetActive(_npcConfig.npcs[i].id == npcId);
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
```

---

### `RaceCameraController.cs`

Handles the pause → lerp → smooth-follow sequence when switching between NPCs.

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CubeRacing
{
    public class RaceCameraController : MonoBehaviour
    {
        [SerializeField] private Camera  _camera;
        [SerializeField] private Vector3 _followOffset   = new Vector3(0f, 8f, -4f);
        [SerializeField] private float   _focusLerpSpeed = 8f;
        [SerializeField] private float   _followLerpSpeed = 5f;
        [SerializeField] private int     _pauseMs        = 300;
        [SerializeField] private float   _arrivalThreshold = 0.1f;

        private Transform _followTarget;
        private bool      _following;

        private void Update()
        {
            if (!_following || _followTarget == null) return;
            var desired = _followTarget.position + _followOffset;
            _camera.transform.position = Vector3.Lerp(
                _camera.transform.position, desired, _followLerpSpeed * Time.deltaTime);
        }

        // Called before each NPC starts moving.
        // Pauses, lerps to the NPC's current position, then starts smooth follow.
        public async UniTask FocusOnAsync(Transform target, CancellationToken ct)
        {
            _following = false;

            await UniTask.Delay(_pauseMs, cancellationToken: ct);

            // Quick lerp until close enough
            while (!ct.IsCancellationRequested)
            {
                var desired = target.position + _followOffset;
                _camera.transform.position = Vector3.Lerp(
                    _camera.transform.position, desired, _focusLerpSpeed * Time.deltaTime);
                if (Vector3.Distance(_camera.transform.position, desired) < _arrivalThreshold)
                    break;
                await UniTask.Yield(ct);
            }

            _followTarget = target;
            _following    = true;
        }
    }
}
```

---

## Changes to Existing Files

### `RaceScope.cs`

```csharp
protected override void Configure(IContainerBuilder builder)
{
    builder.RegisterComponentInHierarchy<BoardController>();
    builder.RegisterComponentInHierarchy<RacePresenter>();
    builder.RegisterComponentInHierarchy<RaceCameraController>();
}
```

### `RacePresenter.cs`

**New serialized fields:**
```csharp
[SerializeField] private RoundToastView     _roundToast;
[SerializeField] private DiceRollPanelView  _dicePanel;
[SerializeField] private RoundBottomHud     _bottomHud;
```

**New injected field** (added to `Construct`):
```csharp
private RaceCameraController _camera;
// in Construct: _camera = camera; (add RaceCameraController camera parameter)
```

**Initialization in `Start()`** (after existing setup):
```csharp
_bottomHud.Initialize(_npcConfig);
```

**Updated `PlayRoundAsync`** top section — insert before the existing `foreach` loop:

```csharp
private async UniTask PlayRoundAsync(RoundExecutedPayload payload, CancellationToken ct)
{
    if (payload.actions == null || payload.actions.Count == 0) return;

    // Build step and dice lookup tables
    var npcSteps = new Dictionary<int, int>();
    var npcDice  = new Dictionary<int, int>();
    foreach (var action in payload.actions)
    {
        int steps = action.toSquare - action.fromSquare;
        npcSteps[action.npcId] = steps;
        npcDice[action.npcId]  = action.diceRoll;
        foreach (var carried in action.carriedNpcIds)
        {
            npcSteps[carried] = steps;
            npcDice[carried]  = action.diceRoll;
        }
    }

    // Round intro sequence
    await _roundToast.ShowAsync(payload.roundNumber, ct);
    await _dicePanel.ShowAsync(payload.actions, _npcConfig, npcDice, ct);
    _bottomHud.SetRound(npcSteps);

    bool hadValidationError = false;

    foreach (var action in payload.actions)
    {
        int steps = action.toSquare - action.fromSquare;
        if (steps <= 0) continue;

        if (!_board.NpcCubes.TryGetValue(action.npcId, out var movingCube)) continue;

        _bottomHud.SetActiveNpc(action.npcId);
        await _camera.FocusOnAsync(movingCube.transform, ct);

        // [rest of existing per-step animation logic — unchanged]
        // ...
    }

    _bottomHud.Hide();

    // [existing _localStacks re-sync and ApplySquareStacks if validation error — unchanged]
}
```

---

## Unity Editor Steps (manual, after code compiles)

1. In RaceScene, create UI GameObjects:
   - `RoundToast` (Canvas child) — wire `_text` TMP_Text
   - `DiceRollPanel` (Canvas child) — 4 `DiceSlot` children, wire `_slots` array; start inactive
   - `BottomHud` (Canvas child) — 4 `RoundHudSlot` children, wire `_slots` array; start inactive
   - `RaceCameraController` — attach to Camera or empty GO; wire `_camera` field
2. Wire new fields in `RacePresenter` Inspector: `_roundToast`, `_dicePanel`, `_bottomHud`
3. Adjust `_followOffset` on `RaceCameraController` to match your scene's camera angle
4. In `RaceConfig.asset`: `npcHeight` defaults to `0.5` — adjust if cubes now overlap
