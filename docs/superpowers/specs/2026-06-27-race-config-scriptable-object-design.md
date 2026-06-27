# RaceConfig ScriptableObject Design

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the internal static `GameSettings` class in `RacePresenter.cs` with a `RaceConfig` ScriptableObject that exposes `stepDuration` (seconds per square) as an Inspector-adjustable field.

**Architecture:** Follow the existing `NpcConfig` pattern — `RaceConfig : ScriptableObject` in `Config/`, assigned to `Main` via `[SerializeField]`, registered with VContainer via `RegisterInstance`, and injected into `RacePresenter` through its `Construct` method.

**Tech Stack:** Unity 6, VContainer, C#

## Global Constraints

- Follow the exact `NpcConfig` registration pattern in `Main.cs` (`RegisterInstance`)
- Do NOT add any fields beyond `stepDuration` (YAGNI)
- `stepDuration` default value: `0.3f` (seconds per square)
- Remove `internal static class GameSettings` and all references to `GameSettings.RoundIntervalMs`; the derived formula `RoundIntervalMs / 1000f * 0.8f / numActions / steps` is deleted entirely
- Asset path convention: `Assets/Scenes/Data/RaceConfig.asset`
- MenuName: `"CubeRacing/RaceConfig"`

---

## Files

| Action | Path |
|--------|------|
| Create | `frontend/unity/Assets/Scripts/Config/RaceConfig.cs` |
| Modify | `frontend/unity/Assets/Scripts/Main.cs` |
| Modify | `frontend/unity/Assets/Scripts/Race/RacePresenter.cs` |
| Unity Editor | Create `Assets/Scenes/Data/RaceConfig.asset` and wire to `Main` Inspector |

---

## Design

### RaceConfig.cs

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

### Main.cs changes

Add alongside the existing `_npcConfig` field:
```csharp
[SerializeField] private RaceConfig _raceConfig;
```

Add null guard in `Configure`:
```csharp
if (_raceConfig == null)
    throw new System.InvalidOperationException("[Main] RaceConfig is not assigned in the Inspector.");
```

Add registration after `builder.RegisterInstance(_npcConfig)`:
```csharp
builder.RegisterInstance(_raceConfig);
```

### RacePresenter.cs changes

**Add field:**
```csharp
private RaceConfig _raceConfig;
```

**Extend `Construct` signature** (add `RaceConfig raceConfig` parameter and assignment):
```csharp
_raceConfig = raceConfig;
```

**In `PlayRoundAsync`**, replace:
```csharp
float durationPerAction = GameSettings.RoundIntervalMs / 1000f * 0.8f / payload.actions.Count;
// ...
float stepDuration = durationPerAction / steps;
await movingCube.MoveToAsync(targetWorld, stepDuration, ct);
```
with:
```csharp
await movingCube.MoveToAsync(targetWorld, _raceConfig.stepDuration, ct);
```

**Delete** the entire `internal static class GameSettings` block at the bottom of `RacePresenter.cs`.

### Unity Editor steps (manual)

1. In the Project window: right-click → Create → CubeRacing → RaceConfig → save as `Assets/Scenes/Data/RaceConfig.asset`
2. Select the `Main` GameObject in the ProjectScope scene
3. Assign the new `RaceConfig.asset` to the `_raceConfig` field in the Inspector
4. Save the scene
