# Frontend–Backend Communication Protocol

This document describes every channel, endpoint, and event the Unity frontend uses to communicate with the .NET backend — organised by game phase.

---

## Overview

Two channels are used in parallel:

| Channel | Protocol | Purpose |
|---|---|---|
| **REST API** | HTTP/HTTPS | Player actions (register, bet) and state polling on scene entry |
| **SignalR** | WebSocket | Real-time push events from server to client |

The SignalR hub is at `/hubs/game`. Messages use the [SignalR JSON protocol](https://learn.microsoft.com/en-us/aspnet/core/signalr/messagepackhubprotocol) — each frame is terminated with the `\x1e` (Record Separator) character and can carry multiple messages separated by the same delimiter.

---

## Authentication

All REST endpoints that require identity read `Authorization: Bearer {token}`, where `{token}` is the player's registration GUID. There is no JWT or session cookie.

```
// Register (no auth required)
POST /api/players
{ "nickname": "Alice" }
→ { "playerId": "...", "token": "...", "chipsBalance": 1000 }

// All subsequent requests
Authorization: Bearer {token}
```

The token is persisted on the client in `PlayerPrefs` and reloaded on app restart.

---

## SignalR Connection & Session Join

After connecting, the client must join a session group to receive per-session events:

```json
// Handshake (sent immediately after WebSocket open)
{ "protocol": "json", "version": 1 }

// Join session group (Hub method invocation, type=1)
{
  "type": 1,
  "invocationId": "0",
  "target": "JoinSession",
  "arguments": ["<sessionId>"]
}
```

The server adds the connection to a SignalR group named after the session ID. All subsequent push events for that session are sent to this group.

**Reconnect behaviour:** On disconnect, the client waits 3 seconds, reconnects, re-sends the handshake, and re-joins the last known session automatically.

**Ping/Pong:** The server sends type-6 ping frames. The client echoes `{ "type": 6 }` immediately.

---

## Betting Phase

### Entry — Polling Current Session

When the Lobby scene loads, the client calls REST to learn the current phase and remaining time. This also covers the case where the client missed the `BettingStarted` event.

```
GET /api/sessions/current
→ 200 CurrentSessionResponse
→ 404 (no active session yet)
```

**`CurrentSessionResponse` schema:**

```json
{
  "sessionId": "guid",
  "status": "Betting",            // Waiting | Betting | Racing | Settling | Completed
  "bettingSecondsRemaining": 42,  // null if not in Betting phase
  "npcOdds": [
    { "npcId": 1, "odds": 1.3 },
    { "npcId": 2, "odds": 2.6 }
  ],
  "mapLength": 20,
  "raceStartsAt": null,           // DateTime (UTC) if Racing phase is imminent; else null
  "bettingStartsAt": null         // DateTime (UTC) if in Waiting phase; else null
}
```

### SignalR Events During Betting

| Event | Payload | Triggered When |
|---|---|---|
| `WaitingStarted` | `{ bettingStartsAt: DateTime }` | A new Waiting phase begins |
| `BettingStarted` | _(no payload)_ | Betting window opens |
| `OddsUpdated` | `[{ npcId, odds }]` | Any player places a bet |
| `BettingEnded` | _(no payload)_ | Betting window closes |

**`WaitingStarted` — payload:**
```json
{ "bettingStartsAt": "2026-06-30T10:00:00Z" }
```
Allows the lobby to show a countdown before betting opens.

**`OddsUpdated` — payload:**
```json
[
  { "npcId": 1, "odds": 1.5 },
  { "npcId": 2, "odds": 3.0 },
  { "npcId": 3, "odds": 1.3 },
  { "npcId": 4, "odds": 2.0 }
]
```
Full odds list for all NPCs is always sent (not a diff).

### Placing a Bet (REST)

```
POST /api/sessions/{sessionId}/bets
Authorization: Bearer {token}
Content-Type: application/json

{ "npcId": 2, "amount": 100 }
```

**Success:** `200 { "success": true }`

**Error responses:**

| HTTP | Body | Meaning |
|---|---|---|
| 409 | `{ "error": "Betting is closed." }` | Phase is not Betting |
| 409 | `{ "error": "You have already placed a bet this session." }` | Duplicate bet |
| 400 | `{ "error": "Insufficient chips." }` | Balance too low |
| 400 | `{ "error": "Invalid NPC ID." }` | Unknown NPC |
| 400 | `{ "error": "Amount must be greater than zero." }` | Zero or negative amount |

After a successful bet, the server broadcasts `OddsUpdated` to all clients in the session group.

---

## Pre-Race Phase (30-second Countdown)

After `BettingEnded`, there is a **30-second window** before round 1 begins. During this time players should navigate to the Race scene and sync NPC positions.

### SignalR: `RaceStarting`

```json
{
  "sessionId": "guid",
  "raceStartsAt": "2026-06-30T10:01:30Z"
}
```

`raceStartsAt` is a UTC timestamp. The client displays a countdown: `now → raceStartsAt`.

**Late-joiner fallback:** If a client misses `RaceStarting` (e.g., scene transition was in progress), it reads `raceStartsAt` from `GET /api/sessions/current`. If that field is non-null, the client computes remaining time as `raceStartsAt - UtcNow`.

### REST: Sync NPC Positions on Scene Entry

When the Race scene loads, the client fetches the current square layout to snap cubes to their correct positions before animations start:

```
GET /api/sessions/current/squares
→ 200 { "3": [2, 1], "7": [4], "12": [3] }   // squareIndex → [npcId (bottom to top)]
→ 204 No Content  (race hasn't started yet — all NPCs still at square 0)
```

The response is a dictionary keyed by square index (as string) mapping to an ordered list of NPC IDs from bottom to top of the stack.

---

## Racing Phase — Per Round

Each round, the server executes all NPC moves and broadcasts the result.

### SignalR: `RoundExecuted`

```json
{
  "sessionId": "guid",
  "roundNumber": 1,
  "actions": [
    {
      "npcId": 3,
      "diceRoll": 4,
      "fromSquare": 0,
      "toSquare": 4,
      "carriedNpcIds": [1]
    },
    {
      "npcId": 2,
      "diceRoll": 2,
      "fromSquare": 0,
      "toSquare": 2,
      "carriedNpcIds": []
    }
  ],
  "squareStacks": {
    "2": [2],
    "4": [3, 1]
  },
  "winner": null
}
```

**Field descriptions:**

| Field | Type | Description |
|---|---|---|
| `roundNumber` | int | 1-based round counter |
| `actions` | array | One entry per NPC, in the order they moved this round |
| `actions[].npcId` | int | The NPC that rolled |
| `actions[].diceRoll` | int | Dice result (1–6) |
| `actions[].fromSquare` | int | Starting square (`-1` = not yet entered the board) |
| `actions[].toSquare` | int | Ending square (same as `fromSquare` if race already finished) |
| `actions[].carriedNpcIds` | int[] | IDs of NPCs carried on top (passengers) |
| `squareStacks` | object | Authoritative board state after this round — key is square index (string), value is NPC IDs bottom-to-top |
| `winner` | int? | NPC ID of the winner if the race ended this round; otherwise `null` |

**Client animation contract:** The client plays `carriedNpcIds` as passengers of the moving NPC — the Unity parent-child transform hierarchy handles this automatically. After all animations finish, `squareStacks` is used to re-sync `_localStacks` and verify the hierarchy is correct.

**Winner detection:** If `winner` is non-null in a `RoundExecuted` payload, the client should prepare for `RaceCompleted` on the next message.

### SignalR: `RaceCompleted`

```json
{ "winnerNpcId": 3 }
```

Sent after the final `RoundExecuted`. Signals that the race is over and settlement is beginning.

---

## Settlement Phase

### SignalR: `SettlementDone`

```json
{
  "winnerNpcId": 3,
  "playerResults": [
    { "playerId": "guid", "nickname": "Alice", "winAmount": 320 },
    { "playerId": "guid", "nickname": "Bob",   "winAmount": 0   }
  ],
  "topLeaderboard": [
    { "nickname": "Alice", "correctBets": 5, "totalChipsWon": 1600 },
    { "nickname": "Bob",   "correctBets": 2, "totalChipsWon": 420  }
  ]
}
```

**Field descriptions:**

| Field | Type | Description |
|---|---|---|
| `winnerNpcId` | int | ID of the winning NPC |
| `playerResults` | array | One entry per bet placed this session |
| `playerResults[].winAmount` | int | Chips awarded (0 if the player backed the wrong NPC, or if nobody backed the winner) |
| `topLeaderboard` | array | Top-20 leaderboard snapshot as of this settlement |

After `SettlementDone`, the server triggers a new Waiting phase and the cycle repeats.

---

## Full Event Sequence (One Complete Session)

```
Client                                      Server
  │                                            │
  │── GET /api/sessions/current ──────────────►│  (scene load, get remaining time)
  │◄─ { status:"Betting", secondsRemaining:42 }│
  │                                            │
  │◄──── SignalR: OddsUpdated ─────────────────│  (another player bet)
  │                                            │
  │── POST /api/sessions/{id}/bets ───────────►│  (player places bet)
  │◄─ { success: true }                        │
  │                                            │
  │◄──── SignalR: OddsUpdated ─────────────────│  (odds updated after our bet)
  │◄──── SignalR: BettingEnded ────────────────│
  │◄──── SignalR: RaceStarting ────────────────│  { raceStartsAt: "..." }
  │                                            │
  │── GET /api/sessions/current/squares ──────►│  (Race scene loaded, sync positions)
  │◄─ { "0": [1,2,3,4] }                       │
  │                                            │
  │◄──── SignalR: RoundExecuted (round 1) ─────│
  │◄──── SignalR: RoundExecuted (round 2) ─────│
  │         ... (N rounds) ...                 │
  │◄──── SignalR: RoundExecuted (winner=3) ────│  (last round, winner declared)
  │◄──── SignalR: RaceCompleted ───────────────│  { winnerNpcId: 3 }
  │◄──── SignalR: SettlementDone ──────────────│  { winnerNpcId, playerResults, topLeaderboard }
  │                                            │
  │◄──── SignalR: WaitingStarted ──────────────│  (next session begins)
```
