# Gameplay & Rules

Cube Racing is a simplified clone of the "小團快跑" mini-game from Wuthering Waves. The server runs fully automated NPC races; players watch and bet on which cube will win.

---

## The Board

The race takes place on a **20-square snake board** (indices 0–19). Square 0 is the starting area (off-board staging); square 19 is the finish line. NPCs move left-to-right along the board, progressing toward square 19.

```
Start → [0][1][2][3]...[18][19] ← Finish
```

`mapLength` is configurable in `GameSettings` (default: 20). All board logic uses this value — nothing is hard-coded.

---

## NPCs

Four NPC cubes participate in every race. They are static configuration defined in `appsettings.json`:

| ID | Name | Color |
|---|---|---|
| 1 | 紅方塊 | `#E53E3E` (Red) |
| 2 | 藍方塊 | `#3182CE` (Blue) |
| 3 | 黃方塊 | `#D69E2E` (Yellow) |
| 4 | 綠方塊 | `#38A169` (Green) |

The same IDs and colors are mirrored in the Unity `NpcConfig` ScriptableObject.

---

## Session Phases

Every race runs through five phases in a continuous loop:

```
[Waiting ~5s] → [Betting 60s] → [Racing] → [Settling] → [Completed] → (repeat)
```

| Phase | Duration | What Happens |
|---|---|---|
| **Waiting** | ~5 s | Server prepares the next session. Players see a countdown to betting. |
| **Betting** | 60 s | Players place exactly one bet per session. Odds update in real time as bets arrive. |
| **Racing** | Variable | NPCs race round-by-round. Each round takes ~3 s. A 30-second pre-race window precedes round 1 so players can enter the race scene. |
| **Settling** | ~1 s | Server calculates winnings and distributes chips. |
| **Completed** | Instant | Session closes. The loop restarts with a new Waiting phase. |

---

## Betting

### Rules

- Each player may place **exactly one bet per session**.
- Bets are accepted only during the **Betting** phase.
- The minimum bet amount is **1 chip**.
- Players start with **1,000 chips** at registration.

### Placing a Bet

Send a POST request during the Betting phase:

```
POST /api/sessions/{sessionId}/bets
Authorization: Bearer {playerToken}

{ "npcId": 2, "amount": 100 }
```

If successful, the server deducts chips, records the bet, and broadcasts updated odds to all connected clients via SignalR (`OddsUpdated`).

### Odds

Odds are **pari-mutuel** — calculated from the total chip pool, not set in advance:

```
odds = totalPool / poolOnWinner
```

The displayed odds represent how many chips a player would receive per chip wagered if their chosen NPC wins. Odds decrease as more chips pile on the same NPC.

Before any bets are placed, the default odds are `1.3` (configurable via `GameSettings.DefaultOdds`).

---

## Race Mechanics

### Rounds

After the 30-second pre-race countdown, the server executes rounds one at a time. Each round, **every NPC rolls a dice** (1–6) and moves forward that many squares. Round results are broadcast in real time via SignalR (`RoundExecuted`).

### Group-Leader Rule (Stacking)

When multiple NPCs occupy the same square, they form a **stack**. The rules for moving a stack:

1. At the start of each round, the NPCs in each stack are shuffled into a random order.
2. The **first NPC in the shuffled order** (the "group leader") is selected to move.
3. The group leader moves forward by its dice roll, **carrying all NPCs above it in the stack**.
4. Other NPCs from the same original stack are skipped for that round — they move along as passengers.

This prevents bunching: once a group leader moves, the remaining members of that initial stack do not get their own turns for that round.

### Stacking on Landing

If an NPC's movement brings it to a square already occupied by other NPCs, it lands **on top of the existing stack**. In subsequent rounds, all NPCs on that square become a new group.

### Winning

The race ends the moment any NPC reaches **square 19** (the finish line). The winner is the **topmost NPC** on the finish square — the last one to have moved onto it (since the moving NPC is appended on top).

---

## Settlement

After the race, the server runs pari-mutuel settlement across all bets:

```
odds      = totalPool / poolOnWinner
winAmount = floor(betAmount × odds)
```

Where:
- `totalPool` = sum of all chips bet in this session
- `poolOnWinner` = sum of chips bet on the winning NPC

**Edge case:** If nobody bet on the winner, `winAmount = 0` for all bets. The pool is not redistributed.

Each player receives their `winAmount` added back to their chip balance. Results are broadcast via SignalR (`SettlementDone`), which includes each player's outcome and a snapshot of the top leaderboard.

---

## Leaderboard

The leaderboard ranks players by **total chips won** (cumulative across all sessions). The top 20 entries are returned by `GET /api/leaderboard` and broadcast in the `SettlementDone` payload at the end of every race.
