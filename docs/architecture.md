# System Architecture

Cube Racing is a real-time multiplayer betting game built on a **.NET 9 backend** and a **Unity 6 frontend**. The backend follows Clean Architecture; communication between the two sides uses a combination of REST (for actions) and SignalR (for real-time push events).

---

## Layer Dependency

```
Domain ← Application ← Infrastructure ← API
```

Each layer may only depend on layers to its left. This prevents domain logic from coupling to database or framework concerns.

| Layer | Package | Responsibility |
|---|---|---|
| **Domain** | `CubeRacing.Domain` | Entities, repository interfaces, domain events, enums — zero external dependencies |
| **Application** | `CubeRacing.Application` | Use cases, `RaceSimulator`, `SettlementCalculator`, application interfaces (`IGameHubNotifier`, `IMessagePublisher`, etc.) |
| **Infrastructure** | `CubeRacing.Infrastructure` | EF Core + MSSQL, RabbitMQ publisher/consumers, SignalR hub, background services |
| **API** | `CubeRacing.API` | Controllers, `TokenAuthMiddleware`, DI composition in `Program.cs` |

---

## Game Lifecycle

```
[Waiting ~5s] → [Betting 60s] → [Racing] → [Settling] → [Completed] → (repeat)
```

`GameSessionManager` is a `BackgroundService` that drives this loop. After the betting window closes it publishes `betting.ended` to RabbitMQ and then **blocks** on `ISessionCompletionSignal.WaitAsync()` until settlement is confirmed. Each phase transition is triggered by a RabbitMQ message, not an in-process timer, which keeps the consumers independently re-deployable.

```
GameSessionManager
  │
  ├─ Creates session → Waiting (30 s wait)
  ├─ StartBetting     → Betting (60 s window open for REST bets)
  └─ Publishes betting.ended ──────────────────────────────────────────────────┐
                                                                               ▼
BettingEndedConsumer  → StartRacing → notify BettingEnded (SignalR)
                      → set RaceStartsAt, notify RaceStarting (SignalR, –30 s)
                      → Task.Delay 30 s
                      → run RaceSimulator rounds (publishes round.executed per round)
                      → publishes race.completed
                             │
                             ▼
RoundExecutedConsumer  → broadcasts RoundExecuted (SignalR) per round
                             │
                             ▼
RaceCompletedConsumer  → SettleSession use case → publishes settlement.done
                             │
                             ▼
SettlementDoneConsumer → broadcasts SettlementDone (SignalR)
                       → ISessionCompletionSignal.Signal()  ──► unblocks GameSessionManager
```

---

## RabbitMQ Pipeline

All consumers extend `RabbitMqConsumerBase` (`BackgroundService`):

- Durable queues, `BasicQos(0, 1, false)` (one message at a time per consumer)
- `AsyncEventingBasicConsumer` with `DispatchConsumersAsync = true` on the connection factory
- 3-attempt retry with 1 s delay; NACK without requeue after 3 failures

| Exchange/Queue | Consumer | Action |
|---|---|---|
| `betting.ended` | `BettingEndedConsumer` | Runs race loop, publishes `round.executed` per round |
| `round.executed` | `RoundExecutedConsumer` | Broadcasts `RoundExecuted` via SignalR |
| `race.completed` | `RaceCompletedConsumer` | Runs `SettleSession`, publishes `settlement.done` |
| `settlement.done` | `SettlementDoneConsumer` | Broadcasts `SettlementDone`, calls `Signal()` |

---

## Key Design Decisions

### Thread Safety

`RabbitMqPublisher` is a singleton; `IModel` (the RabbitMQ channel) is not thread-safe. All publish calls are serialised through a `SemaphoreSlim(1, 1)`.

`ISessionCompletionSignal` uses `SemaphoreSlim(0, 1)`. The release is idempotent — it checks `CurrentCount == 0` before releasing — to prevent double-release panics if the consumer fires twice.

### Transaction Boundary in Application Layer

`PlaceBet` uses `DbContext` (from `Microsoft.EntityFrameworkCore`, registered as `DbContext` in DI) to wrap chip deduction + bet insert + pool update in a single transaction. This keeps the Application layer free of any Infrastructure dependency while still guaranteeing atomicity.

```csharp
// Program.cs
builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>());
```

### Race Start Delay

After betting ends, the backend waits **30 seconds** before starting round 1. This window lets clients enter the race scene and sync their NPC positions before animations begin. The timestamp is broadcast via the `RaceStarting` SignalR event; late-joiners can recover it from `GET /api/sessions/current`.

### NPC Configuration

NPCs are static config, not database rows. Defined in `appsettings.json` under `"Npcs"` and read via `NpcConfig`. The count is also exposed as `GameSettings.NpcCount`, which drives all loop bounds — nothing in the codebase hard-codes `4`.

---

## DI Lifetime Reference

| Service | Lifetime | Reason |
|---|---|---|
| `IConnectionFactory`, `IMessagePublisher`, `IGameHubNotifier`, `ICurrentSessionStore`, `ISessionCompletionSignal` | Singleton | Shared state / long-lived connections |
| Repositories, Use Cases, `SettlementCalculator` | Scoped | Per-request DB context |
| `GameSessionManager`, 4 consumers | Singleton (hosted) | Use `IServiceScopeFactory` to resolve scoped deps |

---

## Authentication

`TokenAuthMiddleware` reads `Authorization: Bearer {guid}`, looks up the player in the DB, and stores the entity in `HttpContext.Items["Player"]`. There is no JWT — the bearer token is the player's registration GUID stored in the `Players` table.
