# Cube Racing — 設計文件

**日期：** 2026-06-23  
**技術棧：** .NET 8.0 / Clean Architecture / RabbitMQ / MSSQL / Unity 6  
**架構方案：** 單體 API ＋ SignalR（方案 A）

---

## 1. 專案概述

仿鳴潮「小團快跑」小遊戲的簡化版，以練習 .NET 後端與 Unity 前後端整合為目標。

玩家以暱稱匿名登入，在下注期間押注某隻 NPC 方塊奪冠，隨後即時觀看比賽直播，結算後更新榜單。所有 NPC 角色皆為伺服器控制，玩家只負責押注與觀看。

---

## 2. 核心玩法

### 地圖與角色

- 地圖為大富翁式格子，共 **20 格**（第 0 格為起點，第 20 格為終點）
- 共 **4 隻 NPC 方塊**（紅、藍、黃、綠），外觀為可愛小方塊
- NPC 設定為靜態常數，不存資料庫

### 堆疊機制

- 當 NPC 落到已有其他方塊的格子時，後到者疊在頂部
- 當底部方塊移動時，帶動其上方的整個堆疊一起前進
- 具體而言：NPC 移動時，取「自身及其上方所有方塊」作為整體前進

### 每回合流程

1. **決定順序：** 本回合 NPC 行動順序隨機決定
2. **擲骰：** 每隻 NPC 擲骰，步數範圍 1–3
3. **依序移動：** 按本回合順序逐一移動（堆疊整體移動）

### 冠軍判定

當任一堆疊抵達第 20 格時，比賽結束，**該堆疊最頂端的方塊**為冠軍。

---

## 3. 遊戲流程與狀態機

```
[Waiting] → [Betting] → [Racing] → [Settling] → [Completed]
    ↑                                                  ↓
    └──────────────── 自動建立下一局 ───────────────────┘
```

| 階段 | 觸發條件 | 持續時間 | 說明 |
|---|---|---|---|
| **Waiting** | 上一局結算完成後自動進入 | ~5 秒 | 顯示結果動畫緩衝 |
| **Betting** | Waiting 結束後自動開始 | 60 秒 | 玩家可下注，賠率即時更新 |
| **Racing** | BettingDeadline 到期 → RabbitMQ 觸發 | 依回合數而定 | 伺服器逐回合計算並廣播 |
| **Settling** | 有方塊抵達終點 | < 1 秒 | 計算彩池賠率、寫 DB、更新榜單 |
| **Completed** | 結算完成 | — | 自動進入下一局的 Waiting |

每回合廣播間隔：伺服器計算完一回合後等待 **1.5 秒**，再廣播 `RoundExecuted`，讓 Unity 有時間播放動畫。

---

## 4. 領域模型

### 聚合根與實體

```
Player
├── PlayerId (GUID)
├── Nickname (string)
├── Token (GUID)           ← 登入憑證
├── ChipsBalance (int)     ← 目前籌碼，初始值 1000
├── TotalChipsWon (int)    ← 累計淨贏籌碼（榜單用）
└── CorrectBets (int)      ← 累計押中次數（榜單用）

GameSession
├── SessionId (GUID)
├── Status (enum: Waiting/Betting/Racing/Settling/Completed)
├── BettingDeadline (DateTime)
├── MapLength (int, default 20)
├── WinnerNpcId (int?, nullable)  ← 結算後填入
└── TotalPool (int)               ← 本局所有下注加總

Bet
├── BetId (GUID)
├── PlayerId (GUID)
├── SessionId (GUID)
├── NpcId (int, 1–4)
├── Amount (int)
└── WinAmount (int?, nullable)    ← 結算後填入

GameRound
├── RoundId (GUID)
├── SessionId (GUID)
├── RoundNumber (int)
└── MovementData (JSON)           ← 該回合快照，用於除錯/重播
```

### NPC 靜態設定（appsettings.json）

```json
"Npcs": [
  { "Id": 1, "Name": "紅方塊", "ColorHex": "#E53E3E" },
  { "Id": 2, "Name": "藍方塊", "ColorHex": "#3182CE" },
  { "Id": 3, "Name": "黃方塊", "ColorHex": "#D69E2E" },
  { "Id": 4, "Name": "綠方塊", "ColorHex": "#38A169" }
]
```

### MSSQL 資料表

| 資料表 | 對應 |
|---|---|
| `Players` | Player 聚合根 |
| `GameSessions` | GameSession 聚合根 |
| `Bets` | Bet 實體（FK → Players, GameSessions） |
| `GameRounds` | 每回合快照（FK → GameSessions） |

---

## 5. REST API

所有需要身份的端點透過 Header 傳遞 Token：`Authorization: Bearer {token}`

| Method | Endpoint | 說明 | Auth |
|---|---|---|---|
| `POST` | `/api/players` | 輸入暱稱，建立玩家，回傳 Token + 初始籌碼 | 不需要 |
| `GET` | `/api/sessions/current` | 目前局狀態、剩餘下注秒數、各 NPC 即時賠率 | 不需要 |
| `POST` | `/api/sessions/{id}/bets` | 下注（Body: `{ npcId, amount }`） | 需要 |
| `GET` | `/api/leaderboard` | 前 20 名（押中次數 + 累計獲利） | 不需要 |

---

## 6. 即時通訊（SignalR）

### GameHub

玩家連線後加入以 `SessionId` 命名的 Group，伺服器向 Group 廣播：

| 事件 | 觸發時機 | Payload 摘要 |
|---|---|---|
| `OddsUpdated` | 有玩家下注時 | `{ npcId: int, odds: float }[]` |
| `BettingEnded` | 下注期結束 | — |
| `RoundExecuted` | 每回合計算完成後 | 見下方詳細結構 |
| `RaceCompleted` | 有方塊抵達終點 | `{ winnerNpcId: int }` |
| `SettlementDone` | 結算完成 | `{ playerResult, topLeaderboard[] }` |

### RoundExecuted Payload

```json
{
  "roundNumber": 3,
  "actions": [
    {
      "npcId": 2,
      "diceRoll": 3,
      "fromSquare": 5,
      "toSquare": 8,
      "carriedNpcIds": [4]
    },
    {
      "npcId": 1,
      "diceRoll": 1,
      "fromSquare": 7,
      "toSquare": 8,
      "carriedNpcIds": []
    }
  ],
  "squareStacks": {
    "8": [2, 4, 1],
    "3": [3]
  }
}
```

- `carriedNpcIds`：跟著一起移動的方塊（在其上方），Unity 用於驅動動畫
- `squareStacks`：回合結束後的完整格子狀態，Unity 用於校正本地狀態；陣列順序為**由下到上**（index 0 為最底層，最後一個 index 為最頂層，即冠軍候選）

---

## 7. RabbitMQ 事件流

使用一個 **Direct Exchange**，四個 Queue：

```
betting.ended      → Consumer: 啟動比賽迴圈（RaceSimulator）
round.executed     → Consumer: 廣播 RoundExecuted（SignalR）
race.completed     → Consumer: 觸發結算邏輯（SettlementCalculator）
settlement.done    → Consumer: 建立下一局 GameSession
```

**使用 RabbitMQ 的理由：** 比賽迴圈是長時間執行的背景任務，API Controller 只需發布事件不需等待；結算保證寫 DB 與廣播的順序性；各階段邏輯解耦，方便獨立測試。

Consumer 例外處理：NACK 後重試最多 3 次，超過則寫入 Dead Letter Queue 並記錄 log。

---

## 8. Clean Architecture 專案結構

```
CubeRacing.sln
├── CubeRacing.Domain
│   ├── Entities/
│   │   ├── Player.cs
│   │   ├── GameSession.cs
│   │   ├── Bet.cs
│   │   └── GameRound.cs
│   ├── ValueObjects/
│   │   ├── NpcId.cs
│   │   └── Token.cs
│   ├── DomainEvents/
│   │   ├── BettingEndedEvent.cs
│   │   ├── RoundExecutedEvent.cs
│   │   ├── RaceCompletedEvent.cs
│   │   └── SettlementDoneEvent.cs
│   └── Interfaces/
│       ├── IPlayerRepository.cs
│       ├── IGameSessionRepository.cs
│       └── IBetRepository.cs
│
├── CubeRacing.Application
│   ├── UseCases/
│   │   ├── CreatePlayer/
│   │   ├── PlaceBet/
│   │   ├── GetCurrentSession/
│   │   └── GetLeaderboard/
│   ├── GameEngine/
│   │   ├── RaceSimulator.cs         ← 骰子、移動、堆疊、冠軍判斷
│   │   └── SettlementCalculator.cs  ← 彩池賠率計算
│   └── EventHandlers/
│       ├── BettingEndedHandler.cs
│       ├── RaceCompletedHandler.cs
│       └── SettlementDoneHandler.cs
│
├── CubeRacing.Infrastructure
│   ├── Persistence/
│   │   ├── AppDbContext.cs
│   │   └── Repositories/
│   ├── Messaging/
│   │   ├── RabbitMqPublisher.cs
│   │   └── Consumers/
│   └── Hubs/
│       └── GameHub.cs
│
└── CubeRacing.API
    ├── Controllers/
    └── Program.cs
```

---

## 9. Unity 客戶端契約

### 連線生命週期

```
啟動
  → POST /api/players（輸入暱稱）→ 取得 Token（存 PlayerPrefs）
  → GET /api/sessions/current   → 取得 SessionId + 目前階段
  → 連線 SignalR GameHub        → 加入 SessionId Group
  → 監聽廣播事件
```

Token 持久化存於 `PlayerPrefs`，下次啟動可跳過暱稱輸入直接恢復身份。

### 各階段 Unity 行為

| 階段 | Unity 行為 |
|---|---|
| **Betting** | 顯示各 NPC 賠率（`OddsUpdated` 即時刷新）；下注 UI 呼叫 REST `POST /bets` |
| **Racing** | 收到 `RoundExecuted` → 播放移動動畫 → 動畫結束後等待下一事件 |
| **Settling** | 收到 `SettlementDone` → 顯示本局輸贏 → 刷新榜單 |
| **重連** | 重連後呼叫 `GET /api/sessions/current` 取完整快照，重建本地格子狀態 |

---

## 10. 彩池賠率計算

```
totalPool = 所有玩家本局下注總和
poolOnNpc(n) = 下注在 NPC n 的金額總和
odds(n) = totalPool / poolOnNpc(n)     （若 poolOnNpc = 0 則賠率顯示 ∞）

玩家贏得金額 = floor(playerBetAmount × odds(winnerNpc))
```

若無人押中冠軍，本局彩池歸零（無人贏得），不做任何轉移。

---

## 11. 錯誤處理

| 情況 | 回應 |
|---|---|
| 下注期已截止 | `409 Conflict` |
| 同一局重複下注 | `409 Conflict` |
| 籌碼不足 | `400 Bad Request` |
| Token 無效 | `401 Unauthorized` |
| 無人下注的局 | 正常跑完，彩池 0，結算跳過，直接開下一局 |

---

## 12. 測試策略

| 層級 | 測試對象 |
|---|---|
| **Domain 單元測試** | `RaceSimulator`（堆疊邏輯、冠軍判斷）、`SettlementCalculator`（賠率計算） |
| **Application 整合測試** | `PlaceBet` Use Case（重複下注、時間過期、餘額不足） |
| **手動測試** | Postman 測 REST；Unity 跑完整一局 |

Infrastructure 層（Repository）不寫自動化測試。
