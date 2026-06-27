# RaceConfig ScriptableObject Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the `internal static class GameSettings` in `RacePresenter.cs` with a `RaceConfig` ScriptableObject that exposes `stepDuration` (seconds per square) as an Inspector-adjustable field.

**Architecture:** New `RaceConfig : ScriptableObject` follows the exact same `NpcConfig` pattern — defined in `Config/`, registered in `Main.cs` via `RegisterInstance`, injected into `RacePresenter` via `Construct`. The hardcoded formula `RoundIntervalMs / 1000f * 0.8f / numActions / steps` is removed; every step of a DOJump uses `_raceConfig.stepDuration` directly.

**Tech Stack:** Unity 6, VContainer, C#

## Global Constraints

- Follow the exact `NpcConfig` registration pattern in `Main.cs` (`RegisterInstance`)
- `RaceConfig` has exactly one field: `public float stepDuration = 0.3f`
- MenuName must be `"CubeRacing/RaceConfig"`
- Asset path: `Assets/Scenes/Data/RaceConfig.asset`
- Remove `internal static class GameSettings` at the bottom of `RacePresenter.cs` entirely
- Remove `durationPerAction` and the local `stepDuration` variables from `PlayRoundAsync`; use `_raceConfig.stepDuration` directly in the `MoveToAsync` call
- No new fields, no new methods beyond what is listed here (YAGNI)

---

### Task 1: C# code changes — RaceConfig, Main, RacePresenter

**Files:**
- Create: `frontend/unity/Assets/Scripts/Config/RaceConfig.cs`
- Modify: `frontend/unity/Assets/Scripts/Main.cs`
- Modify: `frontend/unity/Assets/Scripts/Race/RacePresenter.cs`

**Interfaces:**
- Produces: `RaceConfig` class (public, ScriptableObject) with `public float stepDuration = 0.3f` — consumed by Task 2 (Unity Editor) and by `RacePresenter.Construct`

---

- [ ] **Step 1: Create `RaceConfig.cs`**

Create `frontend/unity/Assets/Scripts/Config/RaceConfig.cs` with this exact content:

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

- [ ] **Step 2: Update `Main.cs`**

Replace the entire contents of `frontend/unity/Assets/Scripts/Main.cs` with:

```csharp
using DG.Tweening;
using MessagePipe;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace CubeRacing
{
    public class Main : LifetimeScope
    {
        private const string ApiBaseUrl = "http://localhost:5062";
        private const string SignalRUrl = "ws://localhost:5062/hubs/game";

        [SerializeField] private NpcConfig  _npcConfig;
        [SerializeField] private RaceConfig _raceConfig;

        protected override void Configure(IContainerBuilder builder)
        {
            DOTween.Init();

            if (_npcConfig == null)
                throw new System.InvalidOperationException("[Main] NpcConfig is not assigned in the Inspector.");
            if (_raceConfig == null)
                throw new System.InvalidOperationException("[Main] RaceConfig is not assigned in the Inspector.");

            // MessagePipe
            builder.RegisterMessagePipe();

            // Setup GlobalMessagePipe to enable diagnostics window and global function
            builder.RegisterBuildCallback(c =>
                GlobalMessagePipe.SetProvider(c.AsServiceProvider()));

            // Network
            builder.Register<ApiClient>(Lifetime.Singleton)
                   .WithParameter("baseUrl", ApiBaseUrl);
            builder.Register<SignalRClient>(Lifetime.Singleton)
                   .WithParameter("url", SignalRUrl);

            // Core
            builder.Register<PlayerSession>(Lifetime.Singleton);
            builder.Register<GameStateService>(Lifetime.Singleton);
            builder.RegisterEntryPoint<AppBootstrapper>();

            // Config
            builder.RegisterInstance(_npcConfig);
            builder.RegisterInstance(_raceConfig);

            // Scene management
            builder.Register<SceneLoader>(Lifetime.Singleton);
        }
    }
}
```

- [ ] **Step 3: Update `RacePresenter.cs`**

Three changes in one edit to `frontend/unity/Assets/Scripts/Race/RacePresenter.cs`:

**3a — Add `_raceConfig` field** alongside the other private fields (after `_board` or `_npcConfig`):

```csharp
private RaceConfig _raceConfig;
```

**3b — Extend `Construct`** — add `RaceConfig raceConfig` as a new parameter (place it after `NpcConfig npcConfig`) and assign it:

The full updated `Construct` method:
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

**3c — Update `PlayRoundAsync`**: remove the `durationPerAction` line and the `float stepDuration` line; replace the `MoveToAsync` call's duration argument with `_raceConfig.stepDuration`.

Replace this block at the top of `PlayRoundAsync`:
```csharp
float durationPerAction  = GameSettings.RoundIntervalMs / 1000f * 0.8f / payload.actions.Count;
bool  hadValidationError = false;

foreach (var action in payload.actions)
{
    int steps = action.toSquare - action.fromSquare;
    if (steps <= 0) continue;

    float stepDuration = durationPerAction / steps;
```

With:
```csharp
bool hadValidationError = false;

foreach (var action in payload.actions)
{
    int steps = action.toSquare - action.fromSquare;
    if (steps <= 0) continue;
```

And change the `MoveToAsync` call from:
```csharp
await movingCube.MoveToAsync(targetWorld, stepDuration, ct);
```
To:
```csharp
await movingCube.MoveToAsync(targetWorld, _raceConfig.stepDuration, ct);
```

**3d — Delete `internal static class GameSettings`**: Remove the entire block at the bottom of the file (lines after `RacePresenter` class closing brace):
```csharp
// Expose RoundIntervalMs as a static constant so RacePresenter can use it
// without referencing the backend's GameSettings class.
internal static class GameSettings
{
    public const int RoundIntervalMs = 1500;
}
```

- [ ] **Step 4: Verify Unity compilation**

Check the Unity Console (`read_console` via MCP, or switch to Unity Editor). Wait for domain reload to complete. Expected: no compile errors. If errors appear, fix them before proceeding.

- [ ] **Step 5: Commit code changes**

```bash
git add frontend/unity/Assets/Scripts/Config/RaceConfig.cs \
        frontend/unity/Assets/Scripts/Main.cs \
        frontend/unity/Assets/Scripts/Race/RacePresenter.cs
git commit -m "feat: replace GameSettings static class with RaceConfig ScriptableObject"
```

---

### Task 2: Unity Editor — create asset and wire Inspector

**Files:**
- Unity Editor: `Assets/Scenes/Data/RaceConfig.asset` (new asset)
- Unity Editor: `Assets/Scenes/Main.unity` (scene modified — Main GameObject gets new field wired)

**Interfaces:**
- Consumes: `RaceConfig` class from Task 1
- Produces: `RaceConfig.asset` wired to the `Main` LifetimeScope Inspector field

---

- [ ] **Step 1: Create the `RaceConfig` asset**

In Unity Editor, in the **Project** window:
1. Navigate to `Assets/Scenes/Data/`
2. Right-click → **Create → CubeRacing → RaceConfig**
3. Name it `RaceConfig` (saves as `RaceConfig.asset`)

The asset appears with `stepDuration = 0.3` in the Inspector. You can adjust the value here (e.g. `0.35` for slightly slower movement).

- [ ] **Step 2: Wire the asset to `Main`**

1. Open the scene that contains the `Main` LifetimeScope GameObject (typically `Assets/Scenes/Main.unity` or the ProjectScope scene — search the Hierarchy for `Main`)
2. Select the `Main` GameObject
3. In the Inspector, find the **Race Config** field (newly added in Task 1)
4. Drag `Assets/Scenes/Data/RaceConfig.asset` into that field

- [ ] **Step 3: Save the scene**

`File → Save` (or Ctrl+S / Cmd+S).

- [ ] **Step 4: Verify no runtime errors**

Enter Play mode. Open the Console. Confirm:
- No `[Main] RaceConfig is not assigned` exception
- No `NullReferenceException` from `_raceConfig` in `RacePresenter`

- [ ] **Step 5: Commit Unity assets**

```bash
git add frontend/unity/Assets/Scenes/Data/RaceConfig.asset \
        frontend/unity/Assets/Scenes/Data/RaceConfig.asset.meta \
        frontend/unity/Assets/Scenes/
git commit -m "feat: add RaceConfig asset and wire to Main Inspector"
```

> Note: stage the scene file(s) that were modified by wiring the Inspector field. Run `git status` to see which `.unity` files changed and include them in the commit.
