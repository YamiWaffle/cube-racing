# Unity Frontend Design — Cube Racing

Date: 2026-06-23

## Overview

2.5D 大富翁風格賽馬投注遊戲的 Unity 前端。玩家登入後在大廳查看 NPC 賠率並下注，比賽開始後可觀看 3D 棋盤上的即時動畫，結算後返回大廳。

Tech: Unity 6, URP, VContainer, R3, UniTask, MessagePipe, DOTween, TextMeshPro, Unity MCP

---

## Scene 架構

三個 Scene，單向流程加返回：

```
LoginScene  →  LobbyScene  ←→  RaceScene
```

- **LoginScene**：純 2D UGUI，驗證 / 建立玩家
- **LobbyScene**：主畫面，2D UGUI
- **RaceScene**：2.5D 3D 棋盤，替換 LobbyScene；比賽結束後返回 Lobby

---

## VContainer Scope 分層

```
ProjectScope (DontDestroyOnLoad)
├── PlayerSession          ← 玩家身分 (token, nickname, chips)
├── ApiClient              ← 所有 REST 呼叫
├── SignalRClient          ← WebSocket 連線 + MessagePipe 發布
└── GameStateService       ← 當前 session 狀態 (R3 ReactiveProperty)

LoginScope   → LoginPresenter
LobbyScope   → LobbyPresenter, BettingDialogPresenter, LeaderboardPresenter
RaceScope    → BoardController, RacePresenter
```

Project scope 跨場景存活；Scene scope 隨場景卸載自動 dispose。

---

## PlayerSession

```
Token:     Guid
PlayerId:  Guid
Nickname:  string
Chips:     ReactiveProperty<int>
```

**PlayerPrefs keys：**

| Key | Type | 說明 |
|---|---|---|
| `player_token` | string (Guid) | 玩家 token |
| `player_nickname` | string | 暱稱 |
| `player_chips` | int | 本地快取，以後端結算值為準 |

---

## 網路層

### ApiClient（Singleton, Project Scope）

`UnityWebRequest` + UniTask，所有請求自動帶 `Authorization: Bearer {token}`：

```
CreatePlayerAsync(nickname)              → POST /api/players
GetCurrentSessionAsync()                 → GET  /api/sessions/current
PlaceBetAsync(sessionId, npcId, amount)  → POST /api/sessions/{id}/bets
GetLeaderboardAsync()                    → GET  /api/leaderboard
```

### SignalRClient（Singleton, Project Scope）

`System.Net.WebSockets.ClientWebSocket` + UniTask 實作 SignalR JSON protocol（不需額外 package）：

- 握手：送 `{"protocol":"json","version":1}\x1e`
- 每筆訊息以 `\x1e`（Record Separator, ASCII 30）結尾
- 收到 ping（type 6）自動回 pong
- 中斷時透過 `GameStateService` 更新狀態 → UI 顯示「連線中斷，重連中...」，並自動嘗試重連

連線後呼叫：
```json
{"type":1,"invocationId":"0","target":"JoinSession","arguments":["<sessionId>"]}
```

收到訊息後透過 MessagePipe 發布：

```
IPublisher<OddsUpdatedMessage>
IPublisher<BettingEndedMessage>
IPublisher<RoundExecutedMessage>
IPublisher<RaceCompletedMessage>
IPublisher<SettlementDoneMessage>
```

### GameStateService（Singleton, Project Scope）

聚合 REST 初始狀態 + SignalR 增量更新：

```
CurrentSessionId:   Guid?
Status:             ReactiveProperty<string>   // Waiting|Betting|Racing|Settling|Completed
SecondsRemaining:   ReactiveProperty<int?>
NpcOdds:            ReactiveProperty<List<NpcOddsDto>>
HasPlacedBet:       ReactiveProperty<bool>
WinnerNpcId:        ReactiveProperty<int?>
IsConnected:        ReactiveProperty<bool>
```

進入 LobbyScene 時呼叫 `GetCurrentSessionAsync()` 填充初始值，之後靠 MessagePipe 訂閱更新。

---

## NPC 設定（來自 appsettings.json）

| Id | Name | ColorHex |
|---|---|---|
| 1 | 紅方塊 | #E53E3E |
| 2 | 藍方塊 | #3182CE |
| 3 | 黃方塊 | #D69E2E |
| 4 | 綠方塊 | #38A169 |

前端靠 `GET /api/sessions/current` 的 `NpcOdds` 推導 NPC 清單（`NpcId` 1–4）。

---

## LoginScene

**流程：**

```
啟動
  ↓
PlayerPrefs 有 token？
  ├─ 有 → 填入 Nickname field（可修改）
  │        顯示「繼續遊戲 (N 籌碼)」按鈕
  │        點擊 → 載入 PlayerSession → LobbyScene
  │
  └─ 沒有 → 空白 Nickname input
             「登入」按鈕
             點擊 → POST /api/players
                  → 存 token + nickname 到 PlayerPrefs
                  → LobbyScene
```

- 等待 API 期間按鈕禁用（灰色）
- API 失敗 → input 下方顯示錯誤訊息

---

## LobbyScene

**UI 佈局：**

```
┌─────────────────────────────────────┐
│  [籌碼: 1000]          [排行榜 🏆]  │
│                                     │
│  ┌─────────────────────────────┐    │
│  │       比賽狀態列             │    │
│  └─────────────────────────────┘    │
│                                     │
│  ┌────┐  ┌────┐  ┌────┐  ┌────┐   │
│  │ 🟥 │  │ 🟦 │  │ 🟨 │  │ 🟩 │   │
│  │紅  │  │藍  │  │黃  │  │綠  │   │
│  │1.5x│  │2.0x│  │1.3x│  │3.0x│   │
│  │[下注]│ │[下注]│ │[下注]│ │[下注]│  │
│  └────┘  └────┘  └────┘  └────┘   │
│                                     │
│         [ 觀看比賽 ]                 │
└─────────────────────────────────────┘
```

**狀態列文字：**

| Status | 顯示 |
|---|---|
| Waiting | 比賽準備中... |
| Betting | 比賽將在 {N} 秒後開始（本地倒數） |
| Racing | 比賽進行中 |
| Settling | 結算中... |
| Completed | {NPC名稱} 獲勝！ |

**NPC Card：**
- `Odds` 永遠有值（無人下注時後端回傳 `DefaultOdds = 1.3`）
- Status ≠ Betting → 「下注」按鈕禁用
- `HasPlacedBet = true` → 所有「下注」按鈕禁用，已下注 NPC 顯示「已下注 ✓」

**「觀看比賽」按鈕：**
- Status = Waiting / Betting → 禁用，文字「尚未開始」
- Status = Racing / Settling / Completed → 啟用，文字「觀看比賽」

**進入 Lobby 流程：**
1. 呼叫 `GetCurrentSessionAsync()` 填充初始狀態
2. 連接 SignalR → `JoinSession(sessionId)`
3. 訂閱 `OddsUpdated` → 更新 NpcOdds
4. 訂閱 `BettingEnded` → Status 切換
5. 訂閱 `SettlementDone` → 更新 Chips，顯示結算通知（贏/輸）
6. Betting 時本地 countdown 每秒 -1（`SecondsRemaining` 作初始值）

---

## 下注 Dialog

點擊 NPC「下注」按鈕後出現模態 Panel：

```
┌──────────────────────────────┐
│  下注：紅方塊                 │
│  當前賠率：1.5x   籌碼：1000  │
│                              │
│  下注金額：[  100  ]          │
│                              │
│  [+10]  [+50]  [+100]  [All] │
│  [清除]                      │
│                              │
│  預計獲得：150 籌碼            │
│                              │
│  [ 確認下注 ]   [ 取消 ]      │
└──────────────────────────────┘
```

**互動邏輯：**
- 面額按鈕疊加金額（可重複點擊）
- 「清除」→ 金額歸零
- All-in → 填入當前籌碼總額
- 超過籌碼餘額 → 自動截斷為最大值
- 「預計獲得」= `Math.Floor(amount * odds)` 即時更新
- 「確認下注」disabled：amount = 0 或 API 呼叫進行中
- API 成功 → 關閉 Dialog，`HasPlacedBet = true`
- API 失敗 → Dialog 內顯示錯誤訊息（不關閉）
- 點擊背景 → 等同「取消」

---

## RaceScene（2.5D 棋盤）

### 棋盤佈局（20 格蛇形，5×4）

```
[20]←[19]←[18]←[17]←[16]  FINISH
                             ↑
[11]→[12]→[13]→[14]→[15]
 ↑
[10]←[09]←[08]←[07]←[06]

[01]→[02]→[03]→[04]→[05]  START
```

### 場景物件

- **Board Tiles**：20 個扁平 Box，交替淺/深色，帶格號文字
- **NPC Cubes**：4 個正方體，材質顏色對應 NPC
- **起終點標記**：特殊顏色 tile + "START" / "FINISH" 文字
- **Camera**：固定 isometric（45° 俯仰 + 45° 水平），不可移動

### RoundExecuted 動畫（依順序）

收到事件後，依 `Actions` 列表**順序逐一**播放：

1. 取 `Action`（`NpcId`, `FromSquare`, `ToSquare`, `CarriedNpcIds`）
2. `NpcId` 及所有 `CarriedNpcIds` 的色塊一起移動到 `ToSquare`（疊加移動）
3. 等待動畫完成後取下一個 Action
4. 每個 Action 動畫時長 = `RoundIntervalMs / Actions.Count`

### RaceCompleted

- 勝利 NPC：scale punch 動畫 + 閃爍
- 顯示「{NPC名稱} 獲勝！」Banner
- 等待 `SettlementDone`

### SettlementDone

- 顯示結算面板：「+{winAmount} 籌碼」或「未中獎」
- 更新 `PlayerSession.Chips`
- 顯示「返回大廳」按鈕

### HUD

```
[ ← 返回大廳（比賽中禁用）]    比賽進行中...    [籌碼: 1000]
```

---

## 排行榜 Panel（Lobby 覆蓋層）

```
┌──────────────────────────────────────┐
│  排行榜                     [ ✕ ]    │
│  # │ 暱稱         │ 累計獲利          │
│  1 │ 大俠         │ 9,800            │
│  3 │ 你的名字 ◀   │ 5,100            │  ← 自己高亮
│  ...                                 │
│  （你不在前 20 名）                    │  ← 不在榜時
└──────────────────────────────────────┘
```

- 開啟時呼叫 `GET /api/leaderboard`
- 以 `PlayerSession.Nickname` 比對高亮自己
- `SettlementDone` 事件附帶 `TopLeaderboard`，自動更新快取
- 點擊 ✕ 或背景關閉

---

## 錯誤處理

| 情況 | 處理方式 |
|---|---|
| API 呼叫失敗 | 就地顯示錯誤訊息，按鈕重新啟用 |
| SignalR 連線中斷 | 狀態列顯示「連線中斷，重連中...」，背景自動重連 |
| 401 Unauthorized | 清除 PlayerPrefs，返回 LoginScene |
| Session 不存在 (404) | 等待 5 秒後重新 poll `GetCurrentSession` |

---

## 後端異動（本次一併完成）

- `GameSettings` 新增 `DefaultOdds: 1.3`
- `NpcOddsDto.Odds` 改為 `double`（非 nullable）
- `GetCurrentSession` 無人下注時回傳 `DefaultOdds` 而非 `null`
- `appsettings.json` 加入 `"DefaultOdds": 1.3`
