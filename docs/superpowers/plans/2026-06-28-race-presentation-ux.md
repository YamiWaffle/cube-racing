# Race Presentation UX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add round toast, dice roll panel, bottom HUD, camera follow, and NPC height config to the race scene to make spectating clearer and more engaging.

**Architecture:** Six independent C# scripts are created as focused MonoBehaviours (RoundToastView, DiceSlotView, DiceRollPanelView, RoundHudSlotView, RoundBottomHud, RaceCameraController). RacePresenter orchestrates them inside PlayRoundAsync. Unity Editor task wires Inspector references and creates scene GameObjects.

**Tech Stack:** Unity 6, VContainer, UniTask, C#

## Global Constraints

- All new MonoBehaviours use `[SerializeField]` for Inspector wiring — no `Find` or `GetComponent` in hot paths
- All async methods accept `CancellationToken ct` and propagate it through every await
- `DiceSlotView` and `RoundHudSlotView` are thin view components — no business logic
- `RaceCameraController` registered in `RaceScope` via `RegisterComponentInHierarchy<RaceCameraController>()`
- `npcHeight` default value: `0.5f` (matches existing hardcoded value — no visual change on day one)
- Do NOT add fields, methods, or parameters beyond those specified here (YAGNI)
- All duration and speed values must be `[SerializeField] float` (or `int` for `_pauseMs`) so they are Inspector-adjustable without recompile
- No unit tests — Unity MonoBehaviours require the Unity runtime. Verification is: compile with no Console errors, then manual Play mode inspection.

---

### Task 1: NPC Height Config — `RaceConfig` + `BoardController`

**Files:**
- Modify: `frontend/unity/Assets/Scripts/Config/RaceConfig.cs`
- Modify: `frontend/unity/Assets/Scripts/Race/BoardController.cs`

**Interfaces:**
- Produces: `RaceConfig.npcHeight: float` — consumed by `BoardController.SpawnNpcCubes` (this task) and `RacePresenter.ApplySquareStacks` + `PlayRoundAsync` (Task 5)

---

- [ ] **Step 1: Add `npcHeight` to `RaceConfig.cs`**

Open `frontend/unity/Assets/Scripts/Config/RaceConfig.cs`. Current content:

```csharp
using UnityEngine;

namespace CubeRacing
{
    [CreateAssetMenu(fileName = "RaceConfig", menuName = "CubeRacing/RaceConfig")]
    public class RaceConfig : ScriptableObject
    {
        [Tooltip("Seconds per square when an NPC jumps one step")]
        public float stepDuration = 0.3f;
    }
}
```

Replace with:

```csharp
using UnityEngine;

namespace CubeRacing
{
    [CreateAssetMenu(fileName = "RaceConfig", menuName = "CubeRacing/RaceConfig")]
    public class RaceConfig : ScriptableObject
    {
        [Tooltip("Seconds per square when an NPC jumps one step")]
        public float stepDuration = 0.3f;

        [Tooltip("Height of each NPC cube (metres). Used for stacking offset calculations.")]
        public float npcHeight = 0.5f;
    }
}
```

- [ ] **Step 2: Inject `RaceConfig` into `BoardController` and update `SpawnNpcCubes`**

Open `frontend/unity/Assets/Scripts/Race/BoardController.cs`.

Replace the existing `Construct` method and `_npcConfig` field declaration:

```csharp
private NpcConfig _npcConfig;

[Inject]
public void Construct(NpcConfig npcConfig) => _npcConfig = npcConfig;
```

With:

```csharp
private NpcConfig  _npcConfig;
private RaceConfig _raceConfig;

[Inject]
public void Construct(NpcConfig npcConfig, RaceConfig raceConfig)
{
    _npcConfig  = npcConfig;
    _raceConfig = raceConfig;
}
```

Then replace the `SpawnNpcCubes` method body — specifically the `offset` line:

```csharp
// Before:
Vector3 offset = new Vector3(i * 0.25f - (count - 1) * 0.125f, i * 0.5f + 0.5f, 0f);

// After:
float h = _raceConfig.npcHeight;
Vector3 offset = new Vector3(i * (h * 0.5f) - (count - 1) * (h * 0.25f), i * h + h, 0f);
```

The full updated `SpawnNpcCubes`:

```csharp
private void SpawnNpcCubes()
{
    Vector3 startPos = _positions[1];
    int     count    = _npcConfig.npcs.Length;
    float   h        = _raceConfig.npcHeight;

    for (int i = 0; i < count; i++)
    {
        var entry  = _npcConfig.npcs[i];
        Vector3 offset = new Vector3(i * (h * 0.5f) - (count - 1) * (h * 0.25f), i * h + h, 0f);
        var cube = Instantiate(_npcCubePrefab, startPos + offset, Quaternion.identity, transform);
        cube.name = $"Npc_{entry.id}";

        var ctrl = cube.GetComponent<NpcCubeController>();
        ctrl.Initialize(entry.id, entry.color);
        NpcCubes[entry.id] = ctrl;
    }
}
```

- [ ] **Step 3: Verify compilation**

Switch to Unity Editor. Wait for the domain reload spinner to complete. Open Window → General → Console. Expected: zero errors. If errors appear, fix before continuing.

- [ ] **Step 4: Commit**

```bash
git add frontend/unity/Assets/Scripts/Config/RaceConfig.cs \
        frontend/unity/Assets/Scripts/Race/BoardController.cs
git commit -m "feat: add npcHeight to RaceConfig, use in BoardController stacking"
```

---

### Task 2: Round Toast + Dice Components

**Files:**
- Create: `frontend/unity/Assets/Scripts/Race/RoundToastView.cs`
- Create: `frontend/unity/Assets/Scripts/Race/DiceSlotView.cs`
- Create: `frontend/unity/Assets/Scripts/Race/DiceRollPanelView.cs`

**Interfaces:**
- Produces:
  - `RoundToastView.ShowAsync(int round, CancellationToken ct): UniTask`
  - `DiceSlotView.Setup(NpcEntry entry)`, `DiceSlotView.SetEmpty()`, `DiceSlotView.RollAsync(int finalValue, float rollDuration, CancellationToken ct): UniTask`
  - `DiceRollPanelView.ShowAsync(NpcConfig npcConfig, Dictionary<int,int> npcDice, CancellationToken ct): UniTask`
- Consumed by: Task 5 (`RacePresenter.PlayRoundAsync`)

---

- [ ] **Step 1: Create `RoundToastView.cs`**

Create `frontend/unity/Assets/Scripts/Race/RoundToastView.cs`:

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

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

- [ ] **Step 2: Create `DiceSlotView.cs`**

Create `frontend/unity/Assets/Scripts/Race/DiceSlotView.cs`:

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

- [ ] **Step 3: Create `DiceRollPanelView.cs`**

Create `frontend/unity/Assets/Scripts/Race/DiceRollPanelView.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CubeRacing
{
    public class DiceRollPanelView : MonoBehaviour
    {
        [SerializeField] private DiceSlotView[] _slots;
        [SerializeField] private float          _rollDuration = 1.2f;
        [SerializeField] private float          _holdDuration = 0.5f;

        public async UniTask ShowAsync(
            NpcConfig npcConfig,
            Dictionary<int, int> npcDice,
            CancellationToken ct)
        {
            gameObject.SetActive(true);

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

- [ ] **Step 4: Verify compilation**

Switch to Unity Editor, wait for domain reload, open Console. Expected: zero errors.

- [ ] **Step 5: Commit**

```bash
git add frontend/unity/Assets/Scripts/Race/RoundToastView.cs \
        frontend/unity/Assets/Scripts/Race/DiceSlotView.cs \
        frontend/unity/Assets/Scripts/Race/DiceRollPanelView.cs
git commit -m "feat: add RoundToastView, DiceSlotView, DiceRollPanelView"
```

---

### Task 3: Bottom HUD

**Files:**
- Create: `frontend/unity/Assets/Scripts/Race/RoundHudSlotView.cs`
- Create: `frontend/unity/Assets/Scripts/Race/RoundBottomHud.cs`

**Interfaces:**
- Produces:
  - `RoundHudSlotView.Setup(NpcEntry entry, int? steps)`, `RoundHudSlotView.SetActive(bool active)`
  - `RoundBottomHud.Initialize(NpcConfig npcConfig)`, `RoundBottomHud.SetRound(Dictionary<int,int> npcSteps)`, `RoundBottomHud.SetActiveNpc(int npcId)`, `RoundBottomHud.Hide()`
- Consumed by: Task 5 (`RacePresenter.Start` and `PlayRoundAsync`)

---

- [ ] **Step 1: Create `RoundHudSlotView.cs`**

Create `frontend/unity/Assets/Scripts/Race/RoundHudSlotView.cs`:

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
        [SerializeField] private Image    _greyOverlay;

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

- [ ] **Step 2: Create `RoundBottomHud.cs`**

Create `frontend/unity/Assets/Scripts/Race/RoundBottomHud.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace CubeRacing
{
    public class RoundBottomHud : MonoBehaviour
    {
        [SerializeField] private RoundHudSlotView[] _slots;

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
                int? steps = npcSteps.TryGetValue(entry.id, out int s) ? s : (int?)null;
                _slots[i].Setup(entry, steps);
                _slots[i].SetActive(false);
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

- [ ] **Step 3: Verify compilation**

Switch to Unity Editor, wait for domain reload, open Console. Expected: zero errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/unity/Assets/Scripts/Race/RoundHudSlotView.cs \
        frontend/unity/Assets/Scripts/Race/RoundBottomHud.cs
git commit -m "feat: add RoundHudSlotView and RoundBottomHud"
```

---

### Task 4: Camera Controller + RaceScope

**Files:**
- Create: `frontend/unity/Assets/Scripts/Race/RaceCameraController.cs`
- Modify: `frontend/unity/Assets/Scripts/Race/RaceScope.cs`

**Interfaces:**
- Produces: `RaceCameraController.FocusOnAsync(Transform target, CancellationToken ct): UniTask`
- Consumed by: Task 5 (`RacePresenter.PlayRoundAsync` inside foreach action loop)

---

- [ ] **Step 1: Create `RaceCameraController.cs`**

Create `frontend/unity/Assets/Scripts/Race/RaceCameraController.cs`:

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CubeRacing
{
    public class RaceCameraController : MonoBehaviour
    {
        [SerializeField] private Camera  _camera;
        [SerializeField] private Vector3 _followOffset    = new Vector3(0f, 8f, -4f);
        [SerializeField] private float   _focusLerpSpeed  = 8f;
        [SerializeField] private float   _followLerpSpeed = 5f;
        [SerializeField] private int     _pauseMs         = 300;
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

        public async UniTask FocusOnAsync(Transform target, CancellationToken ct)
        {
            _following = false;

            await UniTask.Delay(_pauseMs, cancellationToken: ct);

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

- [ ] **Step 2: Register `RaceCameraController` in `RaceScope.cs`**

Open `frontend/unity/Assets/Scripts/Race/RaceScope.cs`. Current content:

```csharp
using VContainer;
using VContainer.Unity;

namespace CubeRacing
{
    public class RaceScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponentInHierarchy<BoardController>();
            builder.RegisterComponentInHierarchy<RacePresenter>();
        }
    }
}
```

Replace with:

```csharp
using VContainer;
using VContainer.Unity;

namespace CubeRacing
{
    public class RaceScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponentInHierarchy<BoardController>();
            builder.RegisterComponentInHierarchy<RacePresenter>();
            builder.RegisterComponentInHierarchy<RaceCameraController>();
        }
    }
}
```

- [ ] **Step 3: Verify compilation**

Switch to Unity Editor, wait for domain reload, open Console. Expected: zero errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/unity/Assets/Scripts/Race/RaceCameraController.cs \
        frontend/unity/Assets/Scripts/Race/RaceScope.cs
git commit -m "feat: add RaceCameraController and register in RaceScope"
```

---

### Task 5: RacePresenter Integration

**Files:**
- Modify: `frontend/unity/Assets/Scripts/Race/RacePresenter.cs`

**Interfaces:**
- Consumes (from Tasks 1–4):
  - `RaceConfig.npcHeight: float`
  - `RoundToastView.ShowAsync(int round, CancellationToken ct): UniTask`
  - `DiceRollPanelView.ShowAsync(List<RoundActionDto>, NpcConfig, Dictionary<int,int>, CancellationToken): UniTask`
  - `RoundBottomHud.Initialize(NpcConfig)`, `.SetRound(Dictionary<int,int>)`, `.SetActiveNpc(int)`, `.Hide()`
  - `RaceCameraController.FocusOnAsync(Transform, CancellationToken): UniTask`

---

- [ ] **Step 1: Add serialized fields and `_camera` private field**

In `RacePresenter.cs`, after the existing `// Winner banner` block of serialized fields (around line 28–29):

```csharp
        // Winner banner
        [SerializeField] private GameObject _winnerBanner;
        [SerializeField] private TMP_Text   _winnerText;
```

Add the three new serialized fields immediately after:

```csharp
        // Round presentation
        [SerializeField] private RoundToastView    _roundToast;
        [SerializeField] private DiceRollPanelView _dicePanel;
        [SerializeField] private RoundBottomHud    _bottomHud;
```

Then, in the private injected fields block (around line 31–40), after `private RaceConfig _raceConfig;`, add:

```csharp
        private RaceCameraController _camera;
```

- [ ] **Step 2: Add `RaceCameraController` parameter to `Construct`**

Replace the existing `Construct` method:

```csharp
        [Inject]
        public void Construct(
            BoardController board, GameStateService gameState,
            PlayerSession session, NpcConfig npcConfig, ApiClient api,
            RaceConfig raceConfig,
            ISubscriber<RoundExecutedMessage>  roundSubscriber,
            ISubscriber<RaceCompletedMessage>  raceCompletedSubscriber,
            ISubscriber<SettlementDoneMessage> settlementSubscriber,
            SceneLoader sceneLoader)
        {
            _board                   = board;
            _gameState               = gameState;
            _session                 = session;
            _npcConfig               = npcConfig;
            _api                     = api;
            _raceConfig              = raceConfig;
            _roundSubscriber         = roundSubscriber;
            _raceCompletedSubscriber = raceCompletedSubscriber;
            _settlementSubscriber    = settlementSubscriber;
            _sceneLoader             = sceneLoader;
        }
```

With:

```csharp
        [Inject]
        public void Construct(
            BoardController board, GameStateService gameState,
            PlayerSession session, NpcConfig npcConfig, ApiClient api,
            RaceConfig raceConfig,
            RaceCameraController camera,
            ISubscriber<RoundExecutedMessage>  roundSubscriber,
            ISubscriber<RaceCompletedMessage>  raceCompletedSubscriber,
            ISubscriber<SettlementDoneMessage> settlementSubscriber,
            SceneLoader sceneLoader)
        {
            _board                   = board;
            _gameState               = gameState;
            _session                 = session;
            _npcConfig               = npcConfig;
            _api                     = api;
            _raceConfig              = raceConfig;
            _camera                  = camera;
            _roundSubscriber         = roundSubscriber;
            _raceCompletedSubscriber = raceCompletedSubscriber;
            _settlementSubscriber    = settlementSubscriber;
            _sceneLoader             = sceneLoader;
        }
```

- [ ] **Step 3: Initialize `_bottomHud` in `Start()`**

In `Start()`, immediately after the line `_backButton.onClick.AddListener(ReturnToLobby);`, add:

```csharp
            _bottomHud.Initialize(_npcConfig);
```

- [ ] **Step 4: Replace `PlayRoundAsync` with the complete new version**

Replace the entire `PlayRoundAsync` method with:

```csharp
        private async UniTask PlayRoundAsync(RoundExecutedPayload payload, CancellationToken ct)
        {
            if (payload.actions == null || payload.actions.Count == 0) return;

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

            await _roundToast.ShowAsync(payload.roundNumber, ct);
            await _dicePanel.ShowAsync(_npcConfig, npcDice, ct);
            _bottomHud.SetRound(npcSteps);

            bool hadValidationError = false;

            foreach (var action in payload.actions)
            {
                int steps = action.toSquare - action.fromSquare;
                if (steps <= 0) continue;

                if (!_board.NpcCubes.TryGetValue(action.npcId, out var movingCube)) continue;

                _bottomHud.SetActiveNpc(action.npcId);
                await _camera.FocusOnAsync(movingCube.transform, ct);

                if (_localStacks.TryGetValue(action.fromSquare, out var fromList))
                {
                    fromList.Remove(action.npcId);
                    foreach (var cId in action.carriedNpcIds)
                        fromList.Remove(cId);
                }

                movingCube.transform.SetParent(null);

                for (int sq = action.fromSquare + 1; sq <= action.toSquare; sq++)
                {
                    int   stackCount  = _localStacks.TryGetValue(sq, out var existing) ? existing.Count : 0;
                    float h           = _raceConfig.npcHeight;
                    var   targetWorld = _board.GetSquarePosition(sq) + Vector3.up * (h + stackCount * h);

                    await movingCube.MoveToAsync(targetWorld, _raceConfig.stepDuration, ct);

                    if (stackCount > 0 && _board.NpcCubes.TryGetValue(_localStacks[sq][^1], out var topNpc))
                        movingCube.transform.SetParent(topNpc.transform);

                    if (sq != action.toSquare)
                        movingCube.transform.SetParent(null);
                }

                if (!_localStacks.TryGetValue(action.toSquare, out var toList))
                    _localStacks[action.toSquare] = toList = new List<int>();
                toList.Add(action.npcId);
                toList.AddRange(action.carriedNpcIds);

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

            _bottomHud.Hide();

            _localStacks.Clear();
            foreach (var (k, v) in payload.squareStacks)
                if (int.TryParse(k, out int sq))
                    _localStacks[sq] = new List<int>(v);

            if (hadValidationError)
                ApplySquareStacks(payload.squareStacks);
        }
```

- [ ] **Step 5: Replace hardcoded `0.5f` in `ApplySquareStacks`**

In `ApplySquareStacks`, replace:

```csharp
                cube.transform.position = sqPos + Vector3.up * 0.5f;
```

With:

```csharp
                cube.transform.position = sqPos + Vector3.up * _raceConfig.npcHeight;
```

And replace:

```csharp
                cube.transform.localPosition = Vector3.up * 0.5f;
```

With:

```csharp
                cube.transform.localPosition = Vector3.up * _raceConfig.npcHeight;
```

- [ ] **Step 6: Verify compilation**

Switch to Unity Editor, wait for domain reload, open Console. Expected: zero errors. If VContainer cannot resolve `RaceCameraController`, confirm the GameObject with that component exists in the RaceScene (it will be added in Task 6, but the script must compile first).

- [ ] **Step 7: Commit**

```bash
git add frontend/unity/Assets/Scripts/Race/RacePresenter.cs
git commit -m "feat: wire race presentation UX into PlayRoundAsync"
```

---

### Task 6: Unity Editor Setup

**Files:**
- Unity Editor: RaceScene (contains Canvas, RacePresenter GO, Camera)

**Interfaces:**
- Consumes (from Tasks 2–5): `RoundToastView`, `DiceRollPanelView`, `RoundBottomHud`, `RaceCameraController`, `RacePresenter` (with new `[SerializeField]` fields)

> **Note:** This task is manual Unity Editor work. Each step must be done in the Unity Editor. After each section, verify in Console that no errors appeared.

---

- [ ] **Step 1: Create `RoundToast` GameObject**

In the RaceScene Hierarchy, find the existing Canvas (the one containing the settlement panel and winner banner).

Under that Canvas, create a new empty GameObject named `RoundToast`:
1. Right-click Canvas → Create Empty → rename to `RoundToast`
2. Set `RoundToast` inactive: in Inspector, uncheck the checkbox next to its name
3. Add component `RoundToastView` to `RoundToast`
4. Inside `RoundToast`, create a child Text (TMP): Right-click `RoundToast` → UI → Text - TextMeshPro → rename to `Label`
5. Configure `Label`:
   - Anchor: center-center, stretch to fill parent
   - Font size: 48, Bold, Center-aligned
   - Text: `Round 1` (placeholder, overwritten at runtime)
6. On `RoundToast`'s `RoundToastView` component in Inspector, drag `Label` into the `Text` field

- [ ] **Step 2: Create `DiceRollPanel` GameObject**

Under the Canvas, create `DiceRollPanel`:
1. Right-click Canvas → Create Empty → rename to `DiceRollPanel`
2. Set inactive by default
3. Add `DiceRollPanelView` component
4. Set a background Image component (optional): add `Image` component, dark semi-transparent color (black, alpha ~0.8), anchor stretch-stretch
5. Add a `HorizontalLayoutGroup` component to `DiceRollPanel` (arranges 4 slots side by side):
   - Spacing: 20, Child Force Expand Width: true

6. Inside `DiceRollPanel`, create 4 child GameObjects named `DiceSlot_1`, `DiceSlot_2`, `DiceSlot_3`, `DiceSlot_4`. For **each** slot:
   a. Add `DiceSlotView` component
   b. Inside the slot, create three children:
      - `ColorBlock` — Image component, set size ~40×40
      - `Name` — TMP_Text, font size 24
      - `Number` — TMP_Text, font size 60, Bold, Center-aligned
   c. On the slot's `DiceSlotView` component, wire: `_colorBlock` → ColorBlock Image, `_nameText` → Name TMP_Text, `_numberText` → Number TMP_Text

7. On `DiceRollPanel`'s `DiceRollPanelView` component, wire `_slots` array: drag DiceSlot_1 through DiceSlot_4 into the 4 elements (Element 0 = DiceSlot_1, …, Element 3 = DiceSlot_4). The order must match `NpcConfig.npcs` order (Red=0, Blue=1, Yellow=2, Green=3).

- [ ] **Step 3: Create `BottomHud` GameObject**

Under the Canvas, create `BottomHud`:
1. Right-click Canvas → Create Empty → rename to `BottomHud`
2. Set inactive by default
3. Add `RoundBottomHud` component
4. Anchor to bottom of screen: set anchor min=(0,0), max=(1,0), pivot=(0.5,0), height=120
5. Add a background Image (dark semi-transparent)
6. Add `HorizontalLayoutGroup` (Spacing: 10, Child Force Expand Width: true)

7. Inside `BottomHud`, create 4 children named `HudSlot_1` through `HudSlot_4`. For **each** slot:
   a. Add `RoundHudSlotView` component
   b. Inside the slot, create:
      - `ColorBlock` — Image, size ~24×24
      - `Name` — TMP_Text, font size 18
      - `Steps` — TMP_Text, font size 36, Bold, Center-aligned
      - `GreyOverlay` — Image, color = (0,0,0) alpha=0.55, anchor stretch-stretch, set **inactive** by default
   c. On the slot's `RoundHudSlotView` component, wire: `_colorBlock`, `_nameText`, `_stepsText`, `_greyOverlay`

8. On `BottomHud`'s `RoundBottomHud` component, wire `_slots` array: HudSlot_1 through HudSlot_4 (same order as NpcConfig.npcs).

- [ ] **Step 4: Add `RaceCameraController` to the scene**

In the Hierarchy, find the Camera GameObject (likely named `Main Camera`):
1. Select `Main Camera`
2. Add Component → search `RaceCameraController` → add it
3. On `RaceCameraController` Inspector:
   - `Camera`: drag the `Main Camera` itself into this field
   - `Follow Offset`: set to `(0, 8, -4)` as starting point (adjust in play mode if needed)
   - `Focus Lerp Speed`: 8
   - `Follow Lerp Speed`: 5
   - `Pause Ms`: 300
   - `Arrival Threshold`: 0.1

- [ ] **Step 5: Wire new fields on `RacePresenter`**

In the Hierarchy, find the GameObject that has the `RacePresenter` component (check under RaceScope or the scene root).

In the Inspector for `RacePresenter`:
- `Round Toast` field → drag `RoundToast` GameObject
- `Dice Panel` field → drag `DiceRollPanel` GameObject
- `Bottom Hud` field → drag `BottomHud` GameObject

- [ ] **Step 6: Save the scene**

File → Save (Cmd+S / Ctrl+S). Confirm scene file shows as modified and is saved.

- [ ] **Step 7: Play mode verification**

Enter Play mode. Navigate to the Race scene. Observe:

1. Before any round starts: no `RoundToast` visible, no `DiceRollPanel`, no `BottomHud`
2. When a round begins:
   - `RoundToast` appears with `Round N` text for ~0.8s, then disappears
   - `DiceRollPanel` appears: all 4 NPC slots show rapidly changing numbers [1-3], then snap to each NPC's actual dice roll. Panel stays ~0.5s then disappears
   - `BottomHud` appears at bottom with all 4 NPCs' step counts, all greyed out
3. For each NPC action:
   - Camera pauses ~300ms, then lerps to follow the moving NPC
   - That NPC's slot in `BottomHud` becomes bright, others grey
   - NPC animates step by step
4. After last action in round: `BottomHud` disappears
5. After all rounds: winner banner appears, then settlement panel (unchanged from before)

If Console shows errors about `RaceCameraController` not found or null refs, confirm the component is attached and Inspector fields are wired.

- [ ] **Step 8: Commit scene changes**

```bash
git status   # confirm which .unity file changed
git add frontend/unity/Assets/Scenes/   # add the modified scene file(s)
git commit -m "feat: add race presentation UI GameObjects and wire Inspector fields"
```

---

## Self-Review Notes

- Task 1 only touches `RaceConfig` + `BoardController`. `RacePresenter` npcHeight replacements are in Task 5 to avoid touching the file twice.
- `RoundBottomHud.SetRound` takes `Dictionary<int,int>` (pre-computed), not `List<RoundActionDto>` directly — matches the signature defined in Task 3 and used in Task 5.
- `DiceRollPanelView.ShowAsync` signature: `(NpcConfig, Dictionary<int,int>, CancellationToken)` — no `actions` parameter; the method only needs `npcDice` (pre-computed in `PlayRoundAsync`) and `npcConfig` to iterate slot order.
- `_greyOverlay` in `RoundHudSlotView.SetActive(false)` activates the overlay (greys it out). Slot is greyed = inactive in game terms = overlay visible. This is consistent with `NpcCardView.SetGreyedOut` pattern.
- Camera `_followOffset` default `(0, 8, -4)` is a starting point. Actual value depends on scene camera setup and should be tuned in play mode.
