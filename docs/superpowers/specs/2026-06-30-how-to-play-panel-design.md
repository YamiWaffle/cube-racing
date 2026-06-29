# How to Play Panel — Design Spec

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `?` icon button in the Lobby that opens a simple overlay panel explaining the 5 core game rules.

**Architecture:** A new `HowToPlayPresenter` MonoBehaviour mirrors the existing `LeaderboardPresenter` pattern — `LobbyPresenter` holds a `[SerializeField]` reference and calls `Show()` on button click. Rule text is static content baked into the Prefab's TMP_Text fields; no runtime data fetching.

**Tech Stack:** Unity 6, uGUI (Canvas / Image / Button / TMP_Text), VContainer (no new injection needed)

---

## Global Constraints

- Follow existing Lobby UI patterns: `HowToPlayPresenter` must be a `MonoBehaviour` wired via `[SerializeField]`, not injected via VContainer.
- No new dependencies, no new ScriptableObjects, no backend calls.
- Rule text is static — written directly in the Prefab's TMP_Text, not generated at runtime.
- `LobbyPresenter.cs` changes must be minimal: one new `[SerializeField]` field + one `onClick.AddListener` line in `Start()`.
- Do not add animations unless they already exist via `UIAnimation` helper; a plain `SetActive` show/hide is acceptable.

---

## UI Layout

### `?` Button

- **Parent:** Lobby Canvas (same root as existing buttons)
- **Anchor:** Bottom-right corner (`anchorMin = (1,0)`, `anchorMax = (1,0)`)
- **Pivot:** `(1, 0)`
- **Size:** 60 × 60 px
- **Content:** TMP_Text label `?`
- **Inspector name:** `HowToPlayButton`

### How to Play Panel Hierarchy

```
[HowToPlayOverlay]          RectTransform — stretch full canvas
  Image                     color: (0, 0, 0, 0.6) — semi-transparent backdrop
  Button                    onClick → HowToPlayPresenter.Hide()
  └─ [Panel]                RectTransform — centered, 480×520 px
       Image                white, rounded corners (sprite optional)
       ├─ [Title]           TMP_Text — "How to Play", bold, font size 28
       ├─ [RulesText]       TMP_Text — 5 rules (see Content section), font size 18, word wrap on
       └─ [CloseButton]     Button — top-right of Panel, "✕" label
            onClick → HowToPlayPresenter.Hide()
```

> The overlay `Button` component sits on `[HowToPlayOverlay]` so clicking outside the Panel also closes it. The `[Panel]` child must have a `Button` component that calls `StopPropagation` — **or** simply place the close logic only on the overlay, and make `[Panel]`'s Image have `Raycast Target = true` to block clicks from passing through. Either approach is acceptable; the implementer should pick whichever is simpler.

---

## Component: `HowToPlayPresenter`

**File:** `frontend/unity/Assets/Scripts/Lobby/HowToPlayPresenter.cs`

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

- `_overlay` points to the `[HowToPlayOverlay]` GameObject.
- `[HowToPlayOverlay]` starts **inactive** in the scene (hidden by default).
- Close Button and overlay Button both call `Hide()` via `onClick` (wired in the Inspector).

---

## Changes to `LobbyPresenter`

**File:** `frontend/unity/Assets/Scripts/Lobby/LobbyPresenter.cs`

Add two fields alongside the existing button fields:

```csharp
[SerializeField] private Button             _howToPlayButton;
[SerializeField] private HowToPlayPresenter _howToPlayPanel;
```

Add one line in `Start()`, alongside the existing button listeners:

```csharp
_howToPlayButton.onClick.AddListener(() => _howToPlayPanel.Show());
```

No other changes to `LobbyPresenter`.

---

## Rule Content (static TMP_Text in Prefab)

The `[RulesText]` TMP_Text contains exactly the following text (line breaks as shown):

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

---

## Inspector Wiring Checklist

| Field on `LobbyPresenter` | Points to |
|---|---|
| `_howToPlayButton` | The `?` Button GameObject |
| `_howToPlayPanel` | The GameObject carrying `HowToPlayPresenter` |

| Field on `HowToPlayPresenter` | Points to |
|---|---|
| `_overlay` | `[HowToPlayOverlay]` GameObject |

| Button | onClick handler |
|---|---|
| `?` Button | `LobbyPresenter` → `_howToPlayPanel.Show()` (via `Start()`) |
| Overlay background Button | `HowToPlayPresenter.Hide()` |
| `[CloseButton]` | `HowToPlayPresenter.Hide()` |

---

## Out of Scope

- Animations (open/close transitions)
- Localization
- Dynamic rule content from backend or ScriptableObject
- Paging or tabs
