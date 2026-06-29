# How to Play Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `?` icon button in the bottom-right corner of the Lobby that opens a simple overlay panel with 5 static game rules.

**Architecture:** `HowToPlayPresenter` is a new MonoBehaviour that shows/hides the overlay. `LobbyPresenter` holds a `[SerializeField]` reference and wires the button in `Start()`. All rule text is static TMP_Text content in the scene — no runtime data fetching.

**Tech Stack:** Unity 6, uGUI (Canvas / Image / Button / TMP_Text), C# MonoBehaviour

## Global Constraints

- `HowToPlayPresenter` must be a `MonoBehaviour` wired via `[SerializeField]`, not injected via VContainer.
- No new ScriptableObjects, no backend calls, no new dependencies.
- `LobbyPresenter.cs` changes: exactly 2 new `[SerializeField]` fields + 1 new `onClick.AddListener` line — nothing else.
- Rule text is static content in TMP_Text; do not generate it at runtime.
- No animations. Plain `SetActive(true/false)` only.

---

## Task 1: Scripts — `HowToPlayPresenter` + `LobbyPresenter` changes

**Files:**
- Create: `frontend/unity/Assets/Scripts/Lobby/HowToPlayPresenter.cs`
- Modify: `frontend/unity/Assets/Scripts/Lobby/LobbyPresenter.cs`

**Interfaces:**
- Produces: `HowToPlayPresenter` class with `public void Show()` and `public void Hide()` — consumed by Task 2 Inspector wiring.

- [ ] **Step 1: Create `HowToPlayPresenter.cs`**

Create the file at `frontend/unity/Assets/Scripts/Lobby/HowToPlayPresenter.cs` with this exact content:

```csharp
using UnityEngine;

namespace CubeRacing
{
    public class HowToPlayPresenter : MonoBehaviour
    {
        [SerializeField] private GameObject _overlay;

        public void Show() => _overlay.SetActive(true);
        public void Hide() => _overlay.SetActive(false);
    }
}
```

- [ ] **Step 2: Add fields to `LobbyPresenter.cs`**

Open `frontend/unity/Assets/Scripts/Lobby/LobbyPresenter.cs`.

Find the block of `[SerializeField]` button fields (around line 20):
```csharp
[SerializeField] private Button                  _watchRaceButton;
[SerializeField] private TMP_Text                _watchRaceButtonText;
[SerializeField] private Button                  _leaderboardButton;
```

Add two new fields immediately after `_leaderboardButton`:
```csharp
[SerializeField] private Button                  _watchRaceButton;
[SerializeField] private TMP_Text                _watchRaceButtonText;
[SerializeField] private Button                  _leaderboardButton;
[SerializeField] private Button                  _howToPlayButton;
[SerializeField] private HowToPlayPresenter      _howToPlayPanel;
```

- [ ] **Step 3: Add listener in `Start()`**

In `LobbyPresenter.cs`, find the `Start()` method. It currently has these two listener lines near the top:
```csharp
_watchRaceButton.onClick.AddListener(OnWatchRaceClicked);
_leaderboardButton.onClick.AddListener(() => _leaderboardPanel.Show(destroyCancellationToken).Forget());
```

Add one line immediately after the leaderboard listener:
```csharp
_watchRaceButton.onClick.AddListener(OnWatchRaceClicked);
_leaderboardButton.onClick.AddListener(() => _leaderboardPanel.Show(destroyCancellationToken).Forget());
_howToPlayButton.onClick.AddListener(() => _howToPlayPanel.Show());
```

- [ ] **Step 4: Verify compilation**

In Unity Editor, wait for compilation to finish (watch the bottom status bar — spinning icon stops). Then open the Console window and confirm there are no errors related to `HowToPlayPresenter` or `LobbyPresenter`.

Expected: zero compile errors.

- [ ] **Step 5: Commit**

```bash
git add frontend/unity/Assets/Scripts/Lobby/HowToPlayPresenter.cs \
        frontend/unity/Assets/Scripts/Lobby/LobbyPresenter.cs
git commit -m "feat: add HowToPlayPresenter and wire LobbyPresenter"
```

---

## Task 2: Unity Editor — Build UI Hierarchy and Wire Inspector

**Files:**
- Modify: `frontend/unity/Assets/Scenes/LobbyScene.unity` (all changes via Unity Editor)

**Interfaces:**
- Consumes: `HowToPlayPresenter.Show()` and `HowToPlayPresenter.Hide()` from Task 1.
- Consumes: `LobbyPresenter._howToPlayButton` and `LobbyPresenter._howToPlayPanel` [SerializeField] slots from Task 1.

### Part A — Add the `?` Button

- [ ] **Step 1: Create the `?` Button**

Open `LobbyScene` in the Unity Editor. In the Hierarchy, find the root Canvas GameObject (the one that contains `_leaderboardButton` and `_watchRaceButton`).

Right-click that Canvas → **UI → Button - TextMeshPro**. Name it `HowToPlayButton`.

- [ ] **Step 2: Position the `?` Button in the bottom-right corner**

Select `HowToPlayButton`. In the Inspector → **RectTransform**:

| Property | Value |
|---|---|
| Anchor Preset | Bottom Right (click the anchor diagram → hold Alt → click bottom-right square) |
| Pivot X / Y | 1 / 0 |
| Anchored Position X | -20 |
| Anchored Position Y | 20 |
| Width | 60 |
| Height | 60 |

- [ ] **Step 3: Set the button label**

Expand `HowToPlayButton` in the Hierarchy → select the child `Text (TMP)`. In the Inspector:

| Property | Value |
|---|---|
| Text | `?` |
| Font Size | 28 |
| Alignment | Center + Middle |

### Part B — Build the Overlay Hierarchy

- [ ] **Step 4: Create `HowToPlayOverlay`**

In the Hierarchy, right-click the root Canvas → **Create Empty**. Name it `HowToPlayOverlay`.

Set its **RectTransform** to stretch full canvas:

| Property | Value |
|---|---|
| Anchor Min | (0, 0) |
| Anchor Max | (1, 1) |
| Left / Right / Top / Bottom offsets | 0 |

Add an **Image** component:
- Color: `(0, 0, 0, 0.6)` — RGBA (0, 0, 0, 153)
- Raycast Target: **true**

Add a **Button** component (no visual transition needed — set `Transition = None`).  
This Button's `onClick` will call `Hide()` — wired in Step 11 below.

- [ ] **Step 5: Create the white `Panel`**

Right-click `HowToPlayOverlay` → **UI → Image**. Name it `Panel`.

**RectTransform:**

| Property | Value |
|---|---|
| Anchor Preset | Middle Center |
| Width | 480 |
| Height | 520 |
| Anchored Position | (0, 0) |

**Image component:**
- Color: white `(1, 1, 1, 1)`
- Raycast Target: **true** ← critical: this blocks clicks on Panel from reaching the overlay Button behind it

- [ ] **Step 6: Add `Title` TMP_Text**

Right-click `Panel` → **UI → Text - TextMeshPro**. Name it `Title`.

**RectTransform:**

| Property | Value |
|---|---|
| Anchor Min | (0, 1) |
| Anchor Max | (1, 1) |
| Pivot | (0.5, 1) |
| Anchored Position Y | -20 |
| Height | 50 |
| Left offset | 20 |
| Right offset | 20 |

**TMP_Text component:**
- Text: `How to Play`
- Font Style: **Bold**
- Font Size: 28
- Alignment: Center + Middle
- Color: black `(0, 0, 0, 1)`

- [ ] **Step 7: Add `RulesText` TMP_Text**

Right-click `Panel` → **UI → Text - TextMeshPro**. Name it `RulesText`.

**RectTransform:**

| Property | Value |
|---|---|
| Anchor Min | (0, 0) |
| Anchor Max | (1, 1) |
| Pivot | (0.5, 0.5) |
| Top offset | 80 |
| Bottom offset | 20 |
| Left offset | 24 |
| Right offset | 24 |

**TMP_Text component:**
- Font Size: 18
- Alignment: Left + Top
- Word Wrap: **enabled**
- Color: `(0.2, 0.2, 0.2, 1)`
- Overflow: **Overflow** (so text isn't clipped if it slightly overruns)
- Text — paste exactly:

```
● Bet
Choose any NPC and wager chips during the 60s betting window.
One bet per race.

● Race
Every round, each NPC rolls a dice (1–3) and moves forward that many squares.

● Stack
When a lower NPC moves, it carries all NPCs above it as passengers.
Carried NPCs still take their own turn later in the same round.

● Win
First NPC to reach the finish line wins.
If multiple NPCs are stacked at the finish, the topmost one wins.

● Payout
If your NPC wins: winnings = bet × odds.
Odds update in real time as bets are placed.
```

- [ ] **Step 8: Add `CloseButton`**

Right-click `Panel` → **UI → Button - TextMeshPro**. Name it `CloseButton`.

**RectTransform:**

| Property | Value |
|---|---|
| Anchor Min | (1, 1) |
| Anchor Max | (1, 1) |
| Pivot | (1, 1) |
| Anchored Position | (-10, -10) |
| Width | 36 |
| Height | 36 |

Child `Text (TMP)`:
- Text: `✕`
- Font Size: 20
- Alignment: Center + Middle

### Part C — Add `HowToPlayPresenter` Component and Wire Everything

- [ ] **Step 9: Add `HowToPlayPresenter` to the Overlay**

Select `HowToPlayOverlay` in the Hierarchy. In the Inspector, click **Add Component** → search for `HowToPlayPresenter` → add it.

In the `HowToPlayPresenter` component, drag `HowToPlayOverlay` itself into the **Overlay** field.

- [ ] **Step 10: Set `HowToPlayOverlay` inactive**

With `HowToPlayOverlay` still selected, uncheck the active checkbox at the very top of the Inspector (the checkbox next to the GameObject name). The overlay must start hidden.

- [ ] **Step 11: Wire close buttons**

**Overlay background close:**  
Select `HowToPlayOverlay`. In the **Button** component → `OnClick` → click `+` → drag `HowToPlayOverlay` into the object slot → from the dropdown select `HowToPlayPresenter → Hide()`.

**Panel close button:**  
Select `CloseButton`. In the **Button** component → `OnClick` → click `+` → drag `HowToPlayOverlay` into the object slot → from the dropdown select `HowToPlayPresenter → Hide()`.

- [ ] **Step 12: Wire `LobbyPresenter` Inspector fields**

In the Hierarchy, find the GameObject that carries the `LobbyPresenter` component (same one that holds `_leaderboardButton` etc.). Select it.

In the Inspector, scroll to the `LobbyPresenter` component. Locate the two new fields added in Task 1:

| Field | Drag in |
|---|---|
| **How To Play Button** | `HowToPlayButton` GameObject |
| **How To Play Panel** | `HowToPlayOverlay` GameObject |

### Part D — Verify and Commit

- [ ] **Step 13: Play mode verification**

Press **Play** in the Unity Editor. In the Game view:

1. Confirm `?` button is visible at the bottom-right corner.
2. Click `?` → the How to Play panel appears centered with rule text visible.
3. Click the `✕` button → panel closes.
4. Click `?` again → panel appears.
5. Click anywhere on the dark overlay outside the white panel → panel closes.
6. Confirm no errors in the Console.

Press **Stop**.

- [ ] **Step 14: Save the scene**

In Unity menu: **File → Save** (or Cmd/Ctrl+S).

- [ ] **Step 15: Commit**

```bash
git add frontend/unity/Assets/Scenes/LobbyScene.unity \
        frontend/unity/Assets/Scenes/LobbyScene.unity.meta 2>/dev/null || true
git commit -m "feat: add How to Play panel UI to LobbyScene"
```
