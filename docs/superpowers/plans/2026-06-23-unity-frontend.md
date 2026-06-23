# Unity Frontend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the full Unity 6 frontend for Cube Racing — login, lobby with NPC betting, and 2.5D race viewer.

**Architecture:** Three scenes (Login → Lobby ↔ Race) with a persistent VContainer ProjectScope holding network and state services. MessagePipe distributes SignalR events; R3 ReactiveProperty drives all UI bindings. The 3D race board uses DOTween for sequential per-action animations driven by `RoundExecuted` events.

**Tech Stack:** Unity 6 (URP), VContainer 1.17, R3 1.3, UniTask 2.5, MessagePipe 1.8, DOTween, TextMeshPro, Newtonsoft.Json, System.Net.WebSockets (built-in .NET 6)

## Global Constraints

- API base URL: `http://localhost:5062` (non-HTTPS to avoid Unity self-signed cert issues in Editor)
- SignalR URL: `ws://localhost:5062/hubs/game`
- All text UI must use TextMeshPro components (not legacy Text)
- All async Unity methods use UniTask, not Task or coroutines
- Namespace: `CubeRacing` for all scripts
- NPC IDs are 1–4; names/colors are defined in `NpcConfig` ScriptableObject
- Buttons must be disabled (interactable=false) while awaiting API responses
- Unity Editor must be open and focused for MCP operations (Tasks 5–11)

---

## File Map

```
Assets/Scripts/
├── Core/
│   ├── Messages.cs           ← MessagePipe message types (all SignalR events)
│   ├── Dtos.cs               ← API request/response types + SignalR payloads
│   ├── PlayerSession.cs      ← Token, nickname, chips (singleton, persists via PlayerPrefs)
│   └── GameStateService.cs   ← Session state ReactiveProperties, MessagePipe subscriber
├── Network/
│   ├── ApiClient.cs          ← REST calls via UnityWebRequest + UniTask
│   └── SignalRClient.cs      ← WebSocket + SignalR JSON protocol + MessagePipe publish
├── Config/
│   └── NpcConfig.cs          ← ScriptableObject: NPC id/name/color definitions
├── Bootstrap/
│   └── Main.cs               ← ProjectScope LifetimeScope (update existing file)
├── Login/
│   ├── LoginScope.cs         ← VContainer scene scope for LoginScene
│   └── LoginPresenter.cs     ← Login UI logic
├── Lobby/
│   ├── LobbyScope.cs         ← VContainer scene scope for LobbyScene
│   ├── LobbyPresenter.cs     ← Status bar, chips, watch-race button, countdown
│   ├── NpcCardView.cs        ← MonoBehaviour on NPC card prefab
│   ├── BettingDialogPresenter.cs  ← Bet amount dialog
│   └── LeaderboardPresenter.cs    ← Leaderboard panel
└── Race/
    ├── RaceScope.cs          ← VContainer scene scope for RaceScene
    ├── BoardController.cs    ← Creates 20 tiles in snake layout, exposes world positions
    ├── NpcCubeController.cs  ← MonoBehaviour on each NPC cube, DOTween movement
    └── RacePresenter.cs      ← Subscribes RoundExecuted/RaceCompleted/SettlementDone, sequences animation

Assets/
├── Scenes/
│   ├── LoginScene.unity
│   ├── LobbyScene.unity
│   └── RaceScene.unity
├── Prefabs/
│   ├── NpcCard.prefab        ← NPC card UI (used ×4 in Lobby)
│   ├── BoardTile.prefab      ← Single flat 3D tile
│   └── NpcCube.prefab        ← Coloured 3D cube for race
└── ScriptableObjects/
    └── NpcConfig.asset
```

---

## Task 1: Newtonsoft.Json + DTOs + Messages

**Files:**
- Modify: `frontend/unity/Packages/manifest.json`
- Create: `frontend/unity/Assets/Scripts/Core/Dtos.cs`
- Create: `frontend/unity/Assets/Scripts/Core/Messages.cs`

**Interfaces:**
- Produces: `CreatePlayerResponse`, `CurrentSessionResponse`, `NpcOddsDto`, `LeaderboardEntry`, `RoundExecutedPayload`, `RoundActionDto`, `SettlementDonePayload`, `PlayerResultDto` — used by ApiClient, SignalRClient, GameStateService
- Produces: `OddsUpdatedMessage`, `BettingEndedMessage`, `RoundExecutedMessage`, `RaceCompletedMessage`, `SettlementDoneMessage` — published by SignalRClient, subscribed by GameStateService and presenters

- [ ] **Step 1: Add Newtonsoft.Json to manifest**

Open `frontend/unity/Packages/manifest.json`. In the `"dependencies"` block, add after `"com.cysharp.unitask"`:

```json
"com.unity.nuget.newtonsoft-json": "3.2.1",
```

- [ ] **Step 2: Wait for Unity to reimport packages**

Switch back to Unity Editor. Unity will detect the manifest change and download + import `Newtonsoft.Json`. Watch the progress bar in the bottom-right. Wait until it completes with no errors in Console.

- [ ] **Step 3: Create Dtos.cs**

Create `Assets/Scripts/Core/Dtos.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace CubeRacing
{
    // --- REST response types ---
    [Serializable]
    public class CreatePlayerResponse
    {
        public Guid playerId;
        public Guid token;
        public int chipsBalance;
    }

    [Serializable]
    public class NpcOddsDto
    {
        public int npcId;
        public double odds;
    }

    [Serializable]
    public class CurrentSessionResponse
    {
        public Guid sessionId;
        public string status;
        public int? bettingSecondsRemaining;
        public List<NpcOddsDto> npcOdds;
        public int mapLength;
    }

    [Serializable]
    public class LeaderboardEntry
    {
        public string nickname;
        public int correctBets;
        public int totalChipsWon;
    }

    // --- SignalR event payloads ---
    [Serializable]
    public class RoundActionDto
    {
        public int npcId;
        public int diceRoll;
        public int fromSquare;
        public int toSquare;
        public List<int> carriedNpcIds;
    }

    [Serializable]
    public class RoundExecutedPayload
    {
        public Guid sessionId;
        public int roundNumber;
        public List<RoundActionDto> actions;
        public Dictionary<string, List<int>> squareStacks;
        public int? winner;
    }

    [Serializable]
    public class PlayerResultDto
    {
        public Guid playerId;
        public string nickname;
        public int winAmount;
    }

    [Serializable]
    public class SettlementDonePayload
    {
        public int winnerNpcId;
        public List<PlayerResultDto> playerResults;
        public List<LeaderboardEntry> topLeaderboard;
    }
}
```

- [ ] **Step 4: Create Messages.cs**

Create `Assets/Scripts/Core/Messages.cs`:

```csharp
using System.Collections.Generic;

namespace CubeRacing
{
    public readonly struct OddsUpdatedMessage
    {
        public readonly List<NpcOddsDto> Odds;
        public OddsUpdatedMessage(List<NpcOddsDto> odds) => Odds = odds;
    }

    public readonly struct BettingEndedMessage { }

    public readonly struct RoundExecutedMessage
    {
        public readonly RoundExecutedPayload Payload;
        public RoundExecutedMessage(RoundExecutedPayload payload) => Payload = payload;
    }

    public readonly struct RaceCompletedMessage
    {
        public readonly int WinnerNpcId;
        public RaceCompletedMessage(int winnerNpcId) => WinnerNpcId = winnerNpcId;
    }

    public readonly struct SettlementDoneMessage
    {
        public readonly SettlementDonePayload Payload;
        public SettlementDoneMessage(SettlementDonePayload payload) => Payload = payload;
    }
}
```

- [ ] **Step 5: Verify no compile errors**

Switch to Unity Editor. Check Console — no red errors. If `Newtonsoft.Json` namespace errors appear, confirm the package imported (Window > Package Manager, check Packages: In Project).

- [ ] **Step 6: Commit**

```bash
git add frontend/unity/Packages/manifest.json \
        frontend/unity/Assets/Scripts/Core/Dtos.cs \
        frontend/unity/Assets/Scripts/Core/Messages.cs
git commit -m "feat: add Newtonsoft.Json and define API DTOs and MessagePipe messages"
```

---

## Task 2: PlayerSession + GameStateService

**Files:**
- Create: `Assets/Scripts/Core/PlayerSession.cs`
- Create: `Assets/Scripts/Core/GameStateService.cs`

**Interfaces:**
- Consumes: `NpcOddsDto`, `OddsUpdatedMessage`, `BettingEndedMessage`, `RaceCompletedMessage`, `SettlementDoneMessage` (from Task 1)
- Produces:
  - `PlayerSession` — `.Token`, `.Nickname`, `.Chips` (ReactiveProperty<int>), `.HasSavedSession`, `.SavedNickname`, `.SavedChips`, `.LoadFromPrefs()`, `.Initialize(playerId, token, nickname, chips)`, `.UpdateChips(chips)`
  - `GameStateService` — `.Status` (ReactiveProperty<string>), `.SecondsRemaining` (ReactiveProperty<int?>), `.NpcOdds` (ReactiveProperty<List<NpcOddsDto>>), `.HasPlacedBet` (ReactiveProperty<bool>), `.WinnerNpcId` (ReactiveProperty<int?>), `.IsConnected` (ReactiveProperty<bool>), `.CurrentSessionId` (Guid), `.MapLength` (int), `.ApplySession(CurrentSessionResponse)`

- [ ] **Step 1: Create PlayerSession.cs**

Create `Assets/Scripts/Core/PlayerSession.cs`:

```csharp
using System;
using R3;
using UnityEngine;

namespace CubeRacing
{
    public class PlayerSession
    {
        private const string KeyToken    = "player_token";
        private const string KeyNickname = "player_nickname";
        private const string KeyChips    = "player_chips";

        public Guid   PlayerId { get; private set; }
        public Guid   Token    { get; private set; }
        public string Nickname { get; private set; }
        public ReactiveProperty<int> Chips { get; } = new(0);

        public bool   HasSavedSession => PlayerPrefs.HasKey(KeyToken);
        public string SavedNickname   => PlayerPrefs.GetString(KeyNickname, string.Empty);
        public int    SavedChips      => PlayerPrefs.GetInt(KeyChips, 0);

        public void LoadFromPrefs()
        {
            Token    = Guid.Parse(PlayerPrefs.GetString(KeyToken));
            Nickname = PlayerPrefs.GetString(KeyNickname);
            Chips.Value = PlayerPrefs.GetInt(KeyChips, 0);
        }

        public void Initialize(Guid playerId, Guid token, string nickname, int chips)
        {
            PlayerId = playerId;
            Token    = token;
            Nickname = nickname;
            Chips.Value = chips;
            Save();
        }

        public void UpdateChips(int chips)
        {
            Chips.Value = chips;
            PlayerPrefs.SetInt(KeyChips, chips);
            PlayerPrefs.Save();
        }

        private void Save()
        {
            PlayerPrefs.SetString(KeyToken,    Token.ToString());
            PlayerPrefs.SetString(KeyNickname, Nickname);
            PlayerPrefs.SetInt(KeyChips,       Chips.Value);
            PlayerPrefs.Save();
        }
    }
}
```

- [ ] **Step 2: Create GameStateService.cs**

Create `Assets/Scripts/Core/GameStateService.cs`:

```csharp
using System;
using System.Collections.Generic;
using MessagePipe;
using R3;
using VContainer.Unity;

namespace CubeRacing
{
    public class GameStateService : IStartable, IDisposable
    {
        public ReactiveProperty<string>        Status           { get; } = new("Waiting");
        public ReactiveProperty<int?>          SecondsRemaining { get; } = new(null);
        public ReactiveProperty<List<NpcOddsDto>> NpcOdds      { get; } = new(new());
        public ReactiveProperty<bool>          HasPlacedBet     { get; } = new(false);
        public ReactiveProperty<int?>          WinnerNpcId      { get; } = new(null);
        public ReactiveProperty<bool>          IsConnected      { get; } = new(false);
        public Guid CurrentSessionId { get; private set; }
        public int  MapLength        { get; private set; }

        private readonly ISubscriber<OddsUpdatedMessage>    _oddsSubscriber;
        private readonly ISubscriber<BettingEndedMessage>   _bettingEndedSubscriber;
        private readonly ISubscriber<RaceCompletedMessage>  _raceCompletedSubscriber;
        private readonly ISubscriber<SettlementDoneMessage> _settlementSubscriber;
        private readonly DisposableBag _bag = new();

        public GameStateService(
            ISubscriber<OddsUpdatedMessage>    oddsSubscriber,
            ISubscriber<BettingEndedMessage>   bettingEndedSubscriber,
            ISubscriber<RaceCompletedMessage>  raceCompletedSubscriber,
            ISubscriber<SettlementDoneMessage> settlementSubscriber)
        {
            _oddsSubscriber          = oddsSubscriber;
            _bettingEndedSubscriber  = bettingEndedSubscriber;
            _raceCompletedSubscriber = raceCompletedSubscriber;
            _settlementSubscriber    = settlementSubscriber;
        }

        public void Start()
        {
            _oddsSubscriber.Subscribe(m =>
                NpcOdds.Value = m.Odds).AddTo(_bag);

            _bettingEndedSubscriber.Subscribe(_ =>
                Status.Value = "Racing").AddTo(_bag);

            _raceCompletedSubscriber.Subscribe(m => {
                Status.Value    = "Completed";
                WinnerNpcId.Value = m.WinnerNpcId;
            }).AddTo(_bag);

            _settlementSubscriber.Subscribe(_ =>
                Status.Value = "Settling").AddTo(_bag);
        }

        public void ApplySession(CurrentSessionResponse session)
        {
            CurrentSessionId      = session.sessionId;
            MapLength             = session.mapLength;
            Status.Value          = session.status;
            SecondsRemaining.Value = session.bettingSecondsRemaining;
            NpcOdds.Value         = session.npcOdds ?? new();
            HasPlacedBet.Value    = false;
            WinnerNpcId.Value     = null;
        }

        public void Dispose() => _bag.Dispose();
    }
}
```

- [ ] **Step 3: Verify in Unity Editor**

Switch to Unity Editor. Console should be clean. If you see R3 or MessagePipe errors, check package versions match manifest.

- [ ] **Step 4: Commit**

```bash
git add frontend/unity/Assets/Scripts/Core/PlayerSession.cs \
        frontend/unity/Assets/Scripts/Core/GameStateService.cs
git commit -m "feat: add PlayerSession and GameStateService with R3 reactive state"
```

---

## Task 3: ApiClient

**Files:**
- Create: `Assets/Scripts/Network/ApiClient.cs`

**Interfaces:**
- Consumes: `CreatePlayerResponse`, `CurrentSessionResponse`, `LeaderboardEntry` (Task 1)
- Produces:
  - `ApiClient(string baseUrl)` — constructor
  - `.SetTokenProvider(Func<string> provider)` — call after login to attach auth token
  - `CreatePlayerAsync(string nickname, CancellationToken) → UniTask<CreatePlayerResponse>`
  - `GetCurrentSessionAsync(CancellationToken) → UniTask<CurrentSessionResponse>`
  - `PlaceBetAsync(Guid sessionId, int npcId, int amount, CancellationToken) → UniTask`
  - `GetLeaderboardAsync(CancellationToken) → UniTask<List<LeaderboardEntry>>`
  - `ApiException(long statusCode, string body)` — thrown on non-2xx

- [ ] **Step 1: Create ApiClient.cs**

Create `Assets/Scripts/Network/ApiClient.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace CubeRacing
{
    public class ApiException : Exception
    {
        public long   StatusCode   { get; }
        public string ResponseBody { get; }
        public ApiException(long statusCode, string body)
            : base($"API {statusCode}: {body}") { StatusCode = statusCode; ResponseBody = body; }
    }

    public class ApiClient
    {
        private readonly string  _baseUrl;
        private Func<string>     _tokenProvider;

        public ApiClient(string baseUrl) => _baseUrl = baseUrl.TrimEnd('/');

        public void SetTokenProvider(Func<string> provider) => _tokenProvider = provider;

        public UniTask<CreatePlayerResponse> CreatePlayerAsync(string nickname, CancellationToken ct = default)
            => PostAsync<CreatePlayerResponse>("/api/players",
               JsonConvert.SerializeObject(new { nickname }), withAuth: false, ct);

        public UniTask<CurrentSessionResponse> GetCurrentSessionAsync(CancellationToken ct = default)
            => GetAsync<CurrentSessionResponse>("/api/sessions/current", ct);

        public async UniTask PlaceBetAsync(Guid sessionId, int npcId, int amount, CancellationToken ct = default)
            => await PostAsync<object>($"/api/sessions/{sessionId}/bets",
               JsonConvert.SerializeObject(new { npcId, amount }), withAuth: true, ct);

        public UniTask<List<LeaderboardEntry>> GetLeaderboardAsync(CancellationToken ct = default)
            => GetAsync<List<LeaderboardEntry>>("/api/leaderboard", ct);

        private async UniTask<T> GetAsync<T>(string path, CancellationToken ct)
        {
            using var req = UnityWebRequest.Get(_baseUrl + path);
            AddAuthHeader(req);
            await req.SendWebRequest().ToUniTask(cancellationToken: ct);
            EnsureSuccess(req);
            return JsonConvert.DeserializeObject<T>(req.downloadHandler.text);
        }

        private async UniTask<T> PostAsync<T>(string path, string json, bool withAuth, CancellationToken ct)
        {
            using var req = new UnityWebRequest(_baseUrl + path, "POST");
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            if (withAuth) AddAuthHeader(req);
            await req.SendWebRequest().ToUniTask(cancellationToken: ct);
            EnsureSuccess(req);
            if (typeof(T) == typeof(object)) return default;
            return JsonConvert.DeserializeObject<T>(req.downloadHandler.text);
        }

        private void AddAuthHeader(UnityWebRequest req)
        {
            var token = _tokenProvider?.Invoke();
            if (!string.IsNullOrEmpty(token))
                req.SetRequestHeader("Authorization", $"Bearer {token}");
        }

        private static void EnsureSuccess(UnityWebRequest req)
        {
            if (req.result != UnityWebRequest.Result.Success)
                throw new ApiException(req.responseCode, req.downloadHandler?.text ?? req.error);
        }
    }
}
```

- [ ] **Step 2: Verify no compile errors in Unity Editor**

- [ ] **Step 3: Commit**

```bash
git add frontend/unity/Assets/Scripts/Network/ApiClient.cs
git commit -m "feat: add ApiClient with UniTask REST wrappers"
```

---

## Task 4: SignalRClient

**Files:**
- Create: `Assets/Scripts/Network/SignalRClient.cs`

**Interfaces:**
- Consumes: All five message types (Task 1); `RoundExecutedPayload`, `SettlementDonePayload` (Task 1)
- Produces:
  - `SignalRClient(string url, IPublisher<...> ×5)` — constructor
  - `ConnectAsync(CancellationToken) → UniTask`
  - `JoinSessionAsync(string sessionId, CancellationToken) → UniTask`
  - `Disconnect()`
  - `IDisposable`

- [ ] **Step 1: Create SignalRClient.cs**

Create `Assets/Scripts/Network/SignalRClient.cs`:

```csharp
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CubeRacing
{
    public class SignalRClient : IDisposable
    {
        private const char Separator = '\x1e';

        private readonly string _url;
        private readonly IPublisher<OddsUpdatedMessage>    _oddsPublisher;
        private readonly IPublisher<BettingEndedMessage>   _bettingEndedPublisher;
        private readonly IPublisher<RoundExecutedMessage>  _roundPublisher;
        private readonly IPublisher<RaceCompletedMessage>  _raceCompletedPublisher;
        private readonly IPublisher<SettlementDoneMessage> _settlementPublisher;

        private ClientWebSocket        _ws;
        private CancellationTokenSource _cts;

        public SignalRClient(
            string url,
            IPublisher<OddsUpdatedMessage>    oddsPublisher,
            IPublisher<BettingEndedMessage>   bettingEndedPublisher,
            IPublisher<RoundExecutedMessage>  roundPublisher,
            IPublisher<RaceCompletedMessage>  raceCompletedPublisher,
            IPublisher<SettlementDoneMessage> settlementPublisher)
        {
            _url                    = url;
            _oddsPublisher          = oddsPublisher;
            _bettingEndedPublisher  = bettingEndedPublisher;
            _roundPublisher         = roundPublisher;
            _raceCompletedPublisher = raceCompletedPublisher;
            _settlementPublisher    = settlementPublisher;
        }

        public async UniTask ConnectAsync(CancellationToken ct = default)
        {
            _ws  = new ClientWebSocket();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var uri = new Uri(_url);
            await _ws.ConnectAsync(uri, _cts.Token);

            await SendRawAsync($"{{\"protocol\":\"json\",\"version\":1}}{Separator}", _cts.Token);
            await ReceiveMessageAsync(_cts.Token); // discard handshake response

            ReceiveLoopAsync(_cts.Token).Forget();
        }

        public async UniTask JoinSessionAsync(string sessionId, CancellationToken ct = default)
        {
            var msg = JsonConvert.SerializeObject(new
            {
                type         = 1,
                invocationId = "0",
                target       = "JoinSession",
                arguments    = new[] { sessionId }
            });
            await SendRawAsync(msg + Separator, ct);
        }

        public void Disconnect()
        {
            _cts?.Cancel();
        }

        private async UniTaskVoid ReceiveLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
            {
                try
                {
                    var message = await ReceiveMessageAsync(ct);
                    if (!string.IsNullOrWhiteSpace(message))
                        ProcessMessage(message);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SignalR] Receive error: {e.Message}");
                    break;
                }
            }
        }

        private void ProcessMessage(string raw)
        {
            var parts = raw.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                try
                {
                    var obj  = JObject.Parse(part);
                    var type = obj["type"]?.Value<int>() ?? 0;

                    if (type == 6) // Ping → Pong
                    {
                        SendRawAsync($"{{\"type\":6}}{Separator}", CancellationToken.None).Forget();
                        continue;
                    }

                    if (type == 1) // Server invocation
                    {
                        var target = obj["target"]?.Value<string>();
                        var args   = obj["arguments"] as JArray;
                        if (args == null || args.Count == 0) continue;

                        switch (target)
                        {
                            case "OddsUpdated":
                                _oddsPublisher.Publish(new OddsUpdatedMessage(
                                    args[0].ToObject<System.Collections.Generic.List<NpcOddsDto>>()));
                                break;
                            case "BettingEnded":
                                _bettingEndedPublisher.Publish(new BettingEndedMessage());
                                break;
                            case "RoundExecuted":
                                _roundPublisher.Publish(new RoundExecutedMessage(
                                    args[0].ToObject<RoundExecutedPayload>()));
                                break;
                            case "RaceCompleted":
                                _raceCompletedPublisher.Publish(new RaceCompletedMessage(
                                    args[0]["winnerNpcId"].Value<int>()));
                                break;
                            case "SettlementDone":
                                _settlementPublisher.Publish(new SettlementDoneMessage(
                                    args[0].ToObject<SettlementDonePayload>()));
                                break;
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SignalR] Parse error: {e.Message} | raw: {part}");
                }
            }
        }

        private async UniTask<string> ReceiveMessageAsync(CancellationToken ct)
        {
            var buffer = new byte[8192];
            var sb     = new StringBuilder();
            WebSocketReceiveResult result;
            do
            {
                result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            } while (!result.EndOfMessage);
            return sb.ToString();
        }

        private async UniTask SendRawAsync(string message, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _ws?.Dispose();
        }
    }
}
```

- [ ] **Step 2: Verify no compile errors in Unity Editor**

- [ ] **Step 3: Commit**

```bash
git add frontend/unity/Assets/Scripts/Network/SignalRClient.cs
git commit -m "feat: add SignalRClient using ClientWebSocket and MessagePipe"
```

---

## Task 5: NpcConfig + VContainer Bootstrap

**Files:**
- Create: `Assets/Scripts/Config/NpcConfig.cs`
- Modify: `Assets/Scripts/Main.cs` (update existing file)

**Interfaces:**
- Produces:
  - `NpcConfig` ScriptableObject — `.Npcs` (array of `NpcEntry { int id; string name; Color color; }`)
  - `NpcConfig.GetById(int id) → NpcEntry`
  - `Main` LifetimeScope — wires all Project-scope singletons

- [ ] **Step 1: Create NpcConfig.cs**

Create `Assets/Scripts/Config/NpcConfig.cs`:

```csharp
using System;
using System.Linq;
using UnityEngine;

namespace CubeRacing
{
    [Serializable]
    public class NpcEntry
    {
        public int    id;
        public string npcName;
        public Color  color;
    }

    [CreateAssetMenu(fileName = "NpcConfig", menuName = "CubeRacing/NpcConfig")]
    public class NpcConfig : ScriptableObject
    {
        public NpcEntry[] npcs = new[]
        {
            new NpcEntry { id = 1, npcName = "紅方塊", color = new Color(0.898f, 0.243f, 0.243f) },
            new NpcEntry { id = 2, npcName = "藍方塊", color = new Color(0.192f, 0.506f, 0.808f) },
            new NpcEntry { id = 3, npcName = "黃方塊", color = new Color(0.839f, 0.620f, 0.180f) },
            new NpcEntry { id = 4, npcName = "綠方塊", color = new Color(0.220f, 0.631f, 0.412f) },
        };

        public NpcEntry GetById(int id) => npcs.FirstOrDefault(n => n.id == id);
    }
}
```

- [ ] **Step 2: Create NpcConfig.asset in Unity Editor**

In Unity Editor Project window: right-click `Assets/ScriptableObjects/` → Create → CubeRacing → NpcConfig. Name it `NpcConfig`. The default values are already populated from the constructor. Verify colors match: 紅=#E53E3E, 藍=#3182CE, 黃=#D69E2E, 綠=#38A169.

- [ ] **Step 3: Update Main.cs**

Replace the entire contents of `Assets/Scripts/Main.cs`:

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
        private const string ApiBaseUrl  = "http://localhost:5062";
        private const string SignalRUrl  = "ws://localhost:5062/hubs/game";

        [SerializeField] private NpcConfig _npcConfig;

        protected override void Configure(IContainerBuilder builder)
        {
            DOTween.Init();

            // MessagePipe
            builder.AddMessagePipe();
            builder.AddMessageBroker<OddsUpdatedMessage>();
            builder.AddMessageBroker<BettingEndedMessage>();
            builder.AddMessageBroker<RoundExecutedMessage>();
            builder.AddMessageBroker<RaceCompletedMessage>();
            builder.AddMessageBroker<SettlementDoneMessage>();

            // Network
            builder.Register<ApiClient>(Lifetime.Singleton)
                   .WithParameter("baseUrl", ApiBaseUrl);
            builder.Register<SignalRClient>(Lifetime.Singleton)
                   .WithParameter("url", SignalRUrl);

            // Core
            builder.Register<PlayerSession>(Lifetime.Singleton);
            builder.RegisterEntryPoint<GameStateService>(Lifetime.Singleton);

            // Config
            builder.RegisterInstance(_npcConfig);
        }
    }
}
```

- [ ] **Step 4: Wire Main in Unity Editor**

In Unity Editor, open `SampleScene` (or whichever scene contains the `Main` component). Find the `Main` GameObject. In the Inspector:
1. Check `Don't Destroy On Load` is enabled on the LifetimeScope component
2. Drag `Assets/ScriptableObjects/NpcConfig.asset` into the `_npcConfig` field

Also set up `VContainerSettings`:
1. In Project window, right-click `Assets/Resources/` → Create → VContainer → VContainerSettings
2. In the VContainerSettings asset, set `Root Lifetime Scope` to the `Main` prefab (or leave blank if Main is always in the first scene)

- [ ] **Step 5: Verify DI wiring**

Enter Play mode in the current scene. If VContainer finds registration errors, they'll appear in Console immediately. Fix any `VContainerException` before proceeding.

- [ ] **Step 6: Commit**

```bash
git add frontend/unity/Assets/Scripts/Config/NpcConfig.cs \
        frontend/unity/Assets/Scripts/Main.cs \
        frontend/unity/Assets/ScriptableObjects/
git commit -m "feat: add NpcConfig ScriptableObject and wire VContainer ProjectScope"
```

---

## Task 6: LoginScene

**Files:**
- Create: `Assets/Scenes/LoginScene.unity` (via Unity Editor)
- Create: `Assets/Scripts/Login/LoginScope.cs`
- Create: `Assets/Scripts/Login/LoginPresenter.cs`

**Interfaces:**
- Consumes: `PlayerSession`, `ApiClient`, `GameStateService` (from ProjectScope)
- Produces: On success, loads `LobbyScene`

- [ ] **Step 1: Create LoginScope.cs**

Create `Assets/Scripts/Login/LoginScope.cs`:

```csharp
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace CubeRacing
{
    public class LoginScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponentInHierarchy<LoginPresenter>();
        }
    }
}
```

- [ ] **Step 2: Create LoginPresenter.cs**

Create `Assets/Scripts/Login/LoginPresenter.cs`:

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VContainer;

namespace CubeRacing
{
    public class LoginPresenter : MonoBehaviour
    {
        [SerializeField] private TMP_InputField _nicknameInput;
        [SerializeField] private Button         _loginButton;
        [SerializeField] private TMP_Text       _errorText;
        [SerializeField] private TMP_Text       _loginButtonText;

        private PlayerSession  _session;
        private ApiClient      _api;
        private GameStateService _gameState;

        [Inject]
        public void Construct(PlayerSession session, ApiClient api, GameStateService gameState)
        {
            _session   = session;
            _api       = api;
            _gameState = gameState;
        }

        private void Start()
        {
            _errorText.text = string.Empty;

            if (_session.HasSavedSession)
            {
                _nicknameInput.text = _session.SavedNickname;
                _loginButtonText.text = $"繼續遊戲 ({_session.SavedChips:N0} 籌碼)";
                _loginButton.onClick.AddListener(() => ContinueAsync(destroyCancellationToken).Forget());
            }
            else
            {
                _loginButtonText.text = "登入";
                _loginButton.onClick.AddListener(() => LoginAsync(destroyCancellationToken).Forget());
            }
        }

        private async UniTaskVoid ContinueAsync(CancellationToken ct)
        {
            _session.LoadFromPrefs();
            _api.SetTokenProvider(() => _session.Token.ToString());
            await LoadLobbyAsync(ct);
        }

        private async UniTaskVoid LoginAsync(CancellationToken ct)
        {
            var nickname = _nicknameInput.text.Trim();
            if (string.IsNullOrEmpty(nickname))
            {
                _errorText.text = "請輸入暱稱";
                return;
            }

            SetLoading(true);
            try
            {
                var result = await _api.CreatePlayerAsync(nickname, ct);
                _session.Initialize(result.playerId, result.token, nickname, result.chipsBalance);
                _api.SetTokenProvider(() => _session.Token.ToString());
                await LoadLobbyAsync(ct);
            }
            catch (ApiException ex)
            {
                _errorText.text = $"登入失敗：{ex.ResponseBody}";
            }
            finally
            {
                SetLoading(false);
            }
        }

        private static async UniTask LoadLobbyAsync(CancellationToken ct)
            => await SceneManager.LoadSceneAsync("LobbyScene").ToUniTask(cancellationToken: ct);

        private void SetLoading(bool loading)
        {
            _loginButton.interactable = !loading;
            if (loading) _loginButtonText.text = "請稍候...";
        }
    }
}
```

- [ ] **Step 3: Build LoginScene in Unity Editor**

1. File > New Scene → Empty. Save as `Assets/Scenes/LoginScene.unity`.
2. Add to Build Settings (File > Build Settings > Add Open Scenes).
3. Scene hierarchy to build:

```
LoginScene
├── Main (copy from SampleScene — the Main LifetimeScope GameObject)
├── LoginScope (empty GameObject, add LoginScope component)
│   └── Set "Parent" field on LifetimeScope component to Main
├── Canvas (Screen Space - Overlay, UI Scale Mode: Scale With Screen Size 1920×1080)
│   ├── Panel (full-screen background, dark color #1A202C)
│   │   └── LoginPanel (centered, ~400×300)
│   │       ├── Title (TMP_Text: "🎲 Cube Racing", font size 36, white)
│   │       ├── NicknameInput (TMP_InputField, placeholder "輸入暱稱...")
│   │       ├── LoginButton (Button, child TMP_Text reference as _loginButtonText)
│   │       └── ErrorText (TMP_Text, red #E53E3E, font size 14, initially empty)
└── EventSystem
```

4. Select `LoginScope` GameObject, drag the `LoginPresenter` component's fields:
   - `_nicknameInput` → NicknameInput
   - `_loginButton` → LoginButton
   - `_errorText` → ErrorText
   - `_loginButtonText` → LoginButton/Text

- [ ] **Step 4: Manual test**

Start backend (`cd backend && dotnet run --project src/CubeRacing.API`). Enter Play mode in LoginScene.
- First run: input field empty, button says "登入" → type a nickname → click → should print no errors and transition to LobbyScene (which doesn't exist yet, so expect a scene-not-found error — that's fine at this stage)
- Second run: button should say "繼續遊戲 (1000 籌碼)" with saved nickname

- [ ] **Step 5: Commit**

```bash
git add frontend/unity/Assets/Scripts/Login/ \
        frontend/unity/Assets/Scenes/LoginScene.unity
git commit -m "feat: add LoginScene with PlayerPrefs-aware login flow"
```

---

## Task 7: LobbyScene + NPC Cards

**Files:**
- Create: `Assets/Scenes/LobbyScene.unity`
- Create: `Assets/Scripts/Lobby/LobbyScope.cs`
- Create: `Assets/Scripts/Lobby/LobbyPresenter.cs`
- Create: `Assets/Scripts/Lobby/NpcCardView.cs`
- Create: `Assets/Prefabs/NpcCard.prefab`

**Interfaces:**
- Consumes: `PlayerSession`, `ApiClient`, `SignalRClient`, `GameStateService`, `NpcConfig`
- Produces: `NpcCardView` (MonoBehaviour on card prefab) with `.SetNpc(NpcEntry, double odds)`, `.SetBettingEnabled(bool)`, `.SetBetPlaced(bool)`, `.OnBetClicked` (Action<int npcId>)

- [ ] **Step 1: Create NpcCardView.cs**

Create `Assets/Scripts/Lobby/NpcCardView.cs`:

```csharp
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CubeRacing
{
    public class NpcCardView : MonoBehaviour
    {
        [SerializeField] private Image    _colorBlock;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _oddsText;
        [SerializeField] private Button   _betButton;
        [SerializeField] private TMP_Text _betButtonText;

        public event Action<int> OnBetClicked;
        private int _npcId;

        private void Awake()
        {
            _betButton.onClick.AddListener(() => OnBetClicked?.Invoke(_npcId));
        }

        public void SetNpc(NpcEntry entry, double odds)
        {
            _npcId            = entry.id;
            _colorBlock.color = entry.color;
            _nameText.text    = entry.npcName;
            _oddsText.text    = $"{odds:F1}x";
            _betButtonText.text = "下注";
        }

        public void UpdateOdds(double odds) => _oddsText.text = $"{odds:F1}x";

        public void SetBettingEnabled(bool enabled) => _betButton.interactable = enabled;

        public void SetBetPlaced(bool placed)
        {
            _betButton.interactable = false;
            if (placed) _betButtonText.text = "已下注 ✓";
        }
    }
}
```

- [ ] **Step 2: Create LobbyScope.cs**

Create `Assets/Scripts/Lobby/LobbyScope.cs`:

```csharp
using VContainer;
using VContainer.Unity;

namespace CubeRacing
{
    public class LobbyScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponentInHierarchy<LobbyPresenter>();
        }
    }
}
```

- [ ] **Step 3: Create LobbyPresenter.cs**

Create `Assets/Scripts/Lobby/LobbyPresenter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VContainer;

namespace CubeRacing
{
    public class LobbyPresenter : MonoBehaviour
    {
        [SerializeField] private TMP_Text       _chipsText;
        [SerializeField] private TMP_Text       _statusText;
        [SerializeField] private Button         _watchRaceButton;
        [SerializeField] private TMP_Text       _watchRaceButtonText;
        [SerializeField] private Button         _leaderboardButton;
        [SerializeField] private Transform      _npcCardsContainer;
        [SerializeField] private NpcCardView    _npcCardPrefab;
        [SerializeField] private BettingDialogPresenter _bettingDialog;
        [SerializeField] private LeaderboardPresenter   _leaderboardPanel;

        private PlayerSession    _session;
        private ApiClient        _api;
        private SignalRClient    _signalR;
        private GameStateService _gameState;
        private NpcConfig        _npcConfig;
        private ISubscriber<OddsUpdatedMessage>    _oddsSubscriber;
        private ISubscriber<SettlementDoneMessage> _settlementSubscriber;

        private readonly List<NpcCardView> _cards     = new();
        private readonly CompositeDisposable _disposables = new();
        private CancellationTokenSource      _countdownCts;

        [Inject]
        public void Construct(
            PlayerSession session, ApiClient api, SignalRClient signalR,
            GameStateService gameState, NpcConfig npcConfig,
            ISubscriber<OddsUpdatedMessage> oddsSubscriber,
            ISubscriber<SettlementDoneMessage> settlementSubscriber)
        {
            _session            = session;
            _api                = api;
            _signalR            = signalR;
            _gameState          = gameState;
            _npcConfig          = npcConfig;
            _oddsSubscriber     = oddsSubscriber;
            _settlementSubscriber = settlementSubscriber;
        }

        private void Start()
        {
            _watchRaceButton.onClick.AddListener(OnWatchRaceClicked);
            _leaderboardButton.onClick.AddListener(() => _leaderboardPanel.Show(destroyCancellationToken).Forget());

            _gameState.Chips.Subscribe(c => _chipsText.text = $"籌碼：{c:N0}").AddTo(_disposables);
            _gameState.Status.Subscribe(OnStatusChanged).AddTo(_disposables);
            _gameState.NpcOdds.Subscribe(OnOddsChanged).AddTo(_disposables);
            _gameState.HasPlacedBet.Subscribe(OnBetPlacedChanged).AddTo(_disposables);
            _oddsSubscriber.Subscribe(m => OnOddsChanged(m.Odds)).AddTo(_disposables);
            _settlementSubscriber.Subscribe(OnSettlementDone).AddTo(_disposables);

            InitAsync(destroyCancellationToken).Forget();
        }

        private async UniTaskVoid InitAsync(CancellationToken ct)
        {
            try
            {
                var session = await _api.GetCurrentSessionAsync(ct);
                _gameState.ApplySession(session);

                SpawnNpcCards(session.npcOdds);

                await _signalR.ConnectAsync(ct);
                await _signalR.JoinSessionAsync(session.sessionId.ToString(), ct);
                _gameState.IsConnected.Value = true;
            }
            catch (Exception e)
            {
                _statusText.text = $"連線失敗：{e.Message}";
            }
        }

        private void SpawnNpcCards(List<NpcOddsDto> odds)
        {
            foreach (Transform child in _npcCardsContainer) Destroy(child.gameObject);
            _cards.Clear();

            foreach (var entry in _npcConfig.npcs)
            {
                var card = Instantiate(_npcCardPrefab, _npcCardsContainer);
                var npcOdds = odds?.Find(o => o.npcId == entry.id);
                card.SetNpc(entry, npcOdds?.odds ?? 1.3);
                card.OnBetClicked += npcId => _bettingDialog.Show(npcId, destroyCancellationToken).Forget();
                _cards.Add(card);
            }
        }

        private void OnStatusChanged(string status)
        {
            _statusText.text = status switch
            {
                "Waiting"   => "比賽準備中...",
                "Betting"   => $"比賽將在 {_gameState.SecondsRemaining.Value ?? 0} 秒後開始",
                "Racing"    => "比賽進行中",
                "Settling"  => "結算中...",
                "Completed" => $"{GetWinnerName()} 獲勝！",
                _           => status
            };

            bool isBetting = status == "Betting";
            bool canWatch  = status is "Racing" or "Settling" or "Completed";

            foreach (var card in _cards)
                card.SetBettingEnabled(isBetting && !_gameState.HasPlacedBet.Value);

            _watchRaceButton.interactable  = canWatch;
            _watchRaceButtonText.text = canWatch ? "觀看比賽" : "尚未開始";

            if (isBetting) StartCountdown();
            else StopCountdown();
        }

        private void OnOddsChanged(List<NpcOddsDto> odds)
        {
            if (odds == null) return;
            foreach (var card in _cards)
            {
                var o = odds.Find(x => x.npcId == _cards.IndexOf(card) + 1);
                if (o != null) card.UpdateOdds(o.odds);
            }
        }

        private void OnBetPlacedChanged(bool placed)
        {
            if (!placed) return;
            foreach (var card in _cards) card.SetBetPlaced(false);
        }

        private void OnSettlementDone(SettlementDoneMessage msg)
        {
            var me = msg.Payload.playerResults?.Find(r => r.nickname == _session.Nickname);
            if (me != null) _session.UpdateChips(_session.Chips.CurrentValue + me.winAmount);
        }

        private void OnWatchRaceClicked()
            => SceneManager.LoadSceneAsync("RaceScene").ToUniTask(cancellationToken: destroyCancellationToken).Forget();

        private void StartCountdown()
        {
            _countdownCts?.Cancel();
            _countdownCts = new CancellationTokenSource();
            CountdownAsync(_countdownCts.Token).Forget();
        }

        private void StopCountdown() => _countdownCts?.Cancel();

        private async UniTaskVoid CountdownAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && (_gameState.SecondsRemaining.Value ?? 0) > 0)
            {
                _statusText.text = $"比賽將在 {_gameState.SecondsRemaining.Value} 秒後開始";
                _gameState.SecondsRemaining.Value--;
                await UniTask.Delay(1000, cancellationToken: ct);
            }
        }

        private string GetWinnerName()
        {
            var id = _gameState.WinnerNpcId.Value;
            return id.HasValue ? _npcConfig.GetById(id.Value)?.npcName ?? "?" : "?";
        }

        private void OnDestroy()
        {
            _disposables.Dispose();
            _countdownCts?.Cancel();
        }
    }
}
```

- [ ] **Step 4: Build NpcCard prefab in Unity Editor**

Create UI prefab at `Assets/Prefabs/NpcCard.prefab`:

```
NpcCard (RectTransform, VerticalLayoutGroup, width=180 height=240)
├── ColorBlock (Image, height=80, color fills from script)
├── NameText (TMP_Text, center, font size 18)
├── OddsText (TMP_Text, center, font size 22, yellow #D69E2E)
└── BetButton (Button, height=44)
    └── BetButtonText (TMP_Text: "下注")
```

Wire `NpcCardView` component on root: drag ColorBlock→`_colorBlock`, NameText→`_nameText`, OddsText→`_oddsText`, BetButton→`_betButton`, BetButtonText→`_betButtonText`.

- [ ] **Step 5: Build LobbyScene in Unity Editor**

New scene, save as `Assets/Scenes/LobbyScene.unity`. Add to Build Settings.

```
LobbyScene
├── Main (same Main LifetimeScope GameObject, DontDestroyOnLoad)
├── LobbyScope (add LobbyScope component; set Parent → Main scope type)
├── Canvas
│   ├── Header
│   │   ├── ChipsText (TMP_Text: "籌碼：1000")
│   │   └── LeaderboardButton (top-right, "排行榜 🏆")
│   ├── StatusBar (Panel, TMP_Text: _statusText)
│   ├── NpcCardsContainer (Horizontal Layout Group, spacing 20)
│   │   └── (NpcCard prefabs spawned at runtime)
│   ├── WatchRaceButton (bottom center, TMP_Text: "尚未開始")
│   ├── BettingDialog (Panel, BettingDialogPresenter — hidden by default)
│   └── LeaderboardPanel (Panel, LeaderboardPresenter — hidden by default)
└── EventSystem
```

Wire `LobbyPresenter` fields on LobbyScope GameObject.

- [ ] **Step 6: Manual test**

Enter Play mode in LobbyScene (backend running). Verify:
- Chips display shows saved value
- Status bar shows current phase text
- 4 NPC cards spawn with correct names, colors, odds
- Betting buttons disabled when not in Betting phase

- [ ] **Step 7: Commit**

```bash
git add frontend/unity/Assets/Scripts/Lobby/LobbyScope.cs \
        frontend/unity/Assets/Scripts/Lobby/LobbyPresenter.cs \
        frontend/unity/Assets/Scripts/Lobby/NpcCardView.cs \
        frontend/unity/Assets/Scenes/LobbyScene.unity \
        frontend/unity/Assets/Prefabs/NpcCard.prefab
git commit -m "feat: add LobbyScene with NPC cards and real-time state binding"
```

---

## Task 8: Betting Dialog

**Files:**
- Create: `Assets/Scripts/Lobby/BettingDialogPresenter.cs`

**Interfaces:**
- Consumes: `PlayerSession`, `ApiClient`, `GameStateService`, `NpcConfig`
- Produces: `BettingDialogPresenter.Show(int npcId, CancellationToken) → UniTaskVoid`

- [ ] **Step 1: Create BettingDialogPresenter.cs**

Create `Assets/Scripts/Lobby/BettingDialogPresenter.cs`:

```csharp
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace CubeRacing
{
    public class BettingDialogPresenter : MonoBehaviour
    {
        [SerializeField] private GameObject _panel;
        [SerializeField] private TMP_Text   _titleText;
        [SerializeField] private TMP_Text   _oddsText;
        [SerializeField] private TMP_Text   _chipsText;
        [SerializeField] private TMP_Text   _amountText;
        [SerializeField] private TMP_Text   _estimatedText;
        [SerializeField] private TMP_Text   _errorText;
        [SerializeField] private Button     _confirmButton;
        [SerializeField] private Button     _cancelButton;
        [SerializeField] private Button     _closeBackdropButton;
        [SerializeField] private Button     _add10Button;
        [SerializeField] private Button     _add50Button;
        [SerializeField] private Button     _add100Button;
        [SerializeField] private Button     _allInButton;
        [SerializeField] private Button     _clearButton;

        private PlayerSession    _session;
        private ApiClient        _api;
        private GameStateService _gameState;
        private NpcConfig        _npcConfig;

        private int    _currentNpcId;
        private double _currentOdds;
        private int    _amount;

        [Inject]
        public void Construct(PlayerSession session, ApiClient api,
                              GameStateService gameState, NpcConfig npcConfig)
        {
            _session   = session;
            _api       = api;
            _gameState = gameState;
            _npcConfig = npcConfig;
        }

        private void Awake()
        {
            _add10Button.onClick.AddListener(() => AddAmount(10));
            _add50Button.onClick.AddListener(() => AddAmount(50));
            _add100Button.onClick.AddListener(() => AddAmount(100));
            _allInButton.onClick.AddListener(AllIn);
            _clearButton.onClick.AddListener(Clear);
            _cancelButton.onClick.AddListener(Hide);
            _closeBackdropButton.onClick.AddListener(Hide);
            _panel.SetActive(false);
        }

        public async UniTaskVoid Show(int npcId, CancellationToken ct)
        {
            _currentNpcId = npcId;
            var entry     = _npcConfig.GetById(npcId);
            var oddsEntry = _gameState.NpcOdds.CurrentValue?.Find(o => o.npcId == npcId);
            _currentOdds  = oddsEntry?.odds ?? 1.3;
            _amount       = 0;
            _errorText.text = string.Empty;

            _titleText.text = $"下注：{entry?.npcName ?? npcId.ToString()}";
            _oddsText.text  = $"當前賠率：{_currentOdds:F1}x";
            RefreshDisplay();
            _panel.SetActive(true);

            _confirmButton.onClick.RemoveAllListeners();
            _confirmButton.onClick.AddListener(() => ConfirmAsync(ct).Forget());
        }

        private void Hide() => _panel.SetActive(false);

        private void AddAmount(int delta)
        {
            _amount = Math.Min(_amount + delta, _session.Chips.CurrentValue);
            RefreshDisplay();
        }

        private void AllIn()
        {
            _amount = _session.Chips.CurrentValue;
            RefreshDisplay();
        }

        private void Clear()
        {
            _amount = 0;
            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            _chipsText.text    = $"籌碼：{_session.Chips.CurrentValue:N0}";
            _amountText.text   = _amount.ToString("N0");
            _estimatedText.text = $"預計獲得：{(int)Math.Floor(_amount * _currentOdds):N0} 籌碼";
            _confirmButton.interactable = _amount > 0;
        }

        private async UniTaskVoid ConfirmAsync(CancellationToken ct)
        {
            _confirmButton.interactable = false;
            _errorText.text = string.Empty;
            try
            {
                await _api.PlaceBetAsync(_gameState.CurrentSessionId, _currentNpcId, _amount, ct);
                _gameState.HasPlacedBet.Value = true;
                Hide();
            }
            catch (ApiException ex)
            {
                _errorText.text = ex.ResponseBody.Contains("already") ? "你已下注過本局" :
                                  ex.ResponseBody.Contains("chips")   ? "籌碼不足"       :
                                  ex.ResponseBody.Contains("closed")  ? "下注時間已結束"  :
                                  $"下注失敗：{ex.ResponseBody}";
                _confirmButton.interactable = _amount > 0;
            }
        }
    }
}
```

- [ ] **Step 2: Build Betting Dialog UI in LobbyScene**

Add as child of Canvas in LobbyScene:

```
BettingDialog (Panel, full-screen semi-transparent backdrop)
├── CloseBackdropButton (full-screen invisible button → _closeBackdropButton)
└── DialogBox (Panel, centered, ~400×380, white background)
    ├── TitleText (TMP_Text → _titleText)
    ├── OddsText (TMP_Text → _oddsText)
    ├── ChipsText (TMP_Text → _chipsText)
    ├── AmountText (TMP_Text, large font → _amountText)
    ├── ButtonRow (Horizontal Layout Group)
    │   ├── Add10Button ("+10" → _add10Button)
    │   ├── Add50Button ("+50" → _add50Button)
    │   ├── Add100Button ("+100" → _add100Button)
    │   └── AllInButton ("All-in" → _allInButton)
    ├── ClearButton ("清除" → _clearButton)
    ├── EstimatedText (TMP_Text → _estimatedText)
    ├── ErrorText (TMP_Text, red → _errorText)
    └── ActionRow
        ├── ConfirmButton ("確認下注" → _confirmButton)
        └── CancelButton ("取消" → _cancelButton)
```

Add `BettingDialogPresenter` component to `BettingDialog` root. Wire all `[SerializeField]` fields. Add to VContainer by registering in LobbyScope:

```csharp
// In LobbyScope.Configure():
builder.RegisterComponentInHierarchy<BettingDialogPresenter>();
```

- [ ] **Step 3: Manual test**

During Betting phase: click a NPC card → dialog opens with correct name/odds → click +10 three times → amount shows 30, estimated shows 30×odds → click 確認下注 → dialog closes, card shows "已下注 ✓".

- [ ] **Step 4: Commit**

```bash
git add frontend/unity/Assets/Scripts/Lobby/BettingDialogPresenter.cs \
        frontend/unity/Assets/Scenes/LobbyScene.unity
git commit -m "feat: add betting dialog with denomination buttons and API integration"
```

---

## Task 9: Leaderboard Panel

**Files:**
- Create: `Assets/Scripts/Lobby/LeaderboardPresenter.cs`

**Interfaces:**
- Consumes: `ApiClient`, `PlayerSession`
- Produces: `LeaderboardPresenter.Show(CancellationToken) → UniTaskVoid`

- [ ] **Step 1: Create LeaderboardPresenter.cs**

Create `Assets/Scripts/Lobby/LeaderboardPresenter.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace CubeRacing
{
    public class LeaderboardPresenter : MonoBehaviour
    {
        [SerializeField] private GameObject _panel;
        [SerializeField] private Transform  _rowContainer;
        [SerializeField] private GameObject _rowPrefab;   // simple TMP_Text row
        [SerializeField] private TMP_Text   _notRankedText;
        [SerializeField] private Button     _closeButton;

        private ApiClient     _api;
        private PlayerSession _session;

        [Inject]
        public void Construct(ApiClient api, PlayerSession session)
        {
            _api     = api;
            _session = session;
        }

        private void Awake()
        {
            _closeButton.onClick.AddListener(Hide);
            _panel.SetActive(false);
        }

        public async UniTaskVoid Show(CancellationToken ct)
        {
            _panel.SetActive(true);
            foreach (Transform child in _rowContainer) Destroy(child.gameObject);
            _notRankedText.text = string.Empty;

            List<LeaderboardEntry> entries;
            try { entries = await _api.GetLeaderboardAsync(ct); }
            catch { _notRankedText.text = "載入失敗"; return; }

            bool selfFound = false;
            for (int i = 0; i < entries.Count; i++)
            {
                var e    = entries[i];
                var row  = Instantiate(_rowPrefab, _rowContainer);
                var text = row.GetComponent<TMP_Text>();
                text.text = $"#{i + 1}  {e.nickname}  {e.totalChipsWon:N0}";

                if (e.nickname == _session.Nickname)
                {
                    text.color = new Color(0.839f, 0.620f, 0.180f); // highlight gold
                    selfFound  = true;
                }
            }

            if (!selfFound)
                _notRankedText.text = "（你不在前 20 名）";
        }

        private void Hide() => _panel.SetActive(false);
    }
}
```

- [ ] **Step 2: Build Leaderboard UI in LobbyScene**

```
LeaderboardPanel (Panel, full-screen backdrop)
└── DialogBox (centered, ~480×600)
    ├── Header
    │   ├── TitleText (TMP_Text: "排行榜")
    │   └── CloseButton ("✕" → _closeButton)
    ├── RowContainer (VerticalLayoutGroup, scroll view → _rowContainer)
    └── NotRankedText (TMP_Text, bottom → _notRankedText)
```

Create `LeaderboardRow.prefab`: a single `TMP_Text` prefab for each row. Wire `_rowPrefab` to it.

Add `LeaderboardPresenter` component, wire fields. Add to LobbyScope:

```csharp
// In LobbyScope.Configure():
builder.RegisterComponentInHierarchy<LeaderboardPresenter>();
```

- [ ] **Step 3: Manual test**

Click 排行榜 button → panel opens → shows top 20 players, self highlighted in gold if present → ✕ closes it.

- [ ] **Step 4: Commit**

```bash
git add frontend/unity/Assets/Scripts/Lobby/LeaderboardPresenter.cs \
        frontend/unity/Assets/Scenes/LobbyScene.unity
git commit -m "feat: add leaderboard panel with self-highlighting"
```

---

## Task 10: RaceScene Board

**Files:**
- Create: `Assets/Scenes/RaceScene.unity`
- Create: `Assets/Scripts/Race/RaceScope.cs`
- Create: `Assets/Scripts/Race/BoardController.cs`
- Create: `Assets/Scripts/Race/NpcCubeController.cs`
- Create: `Assets/Prefabs/BoardTile.prefab`
- Create: `Assets/Prefabs/NpcCube.prefab`

**Interfaces:**
- Produces:
  - `BoardController.GetSquarePosition(int squareIndex) → Vector3` (1-based, returns world-space center)
  - `BoardController.NpcCubes` — `Dictionary<int, NpcCubeController>` keyed by npcId
  - `NpcCubeController.MoveToAsync(Vector3 target, float duration) → UniTask`

- [ ] **Step 1: Create RaceScope.cs**

Create `Assets/Scripts/Race/RaceScope.cs`:

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

- [ ] **Step 2: Create BoardController.cs**

Create `Assets/Scripts/Race/BoardController.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;
using VContainer;

namespace CubeRacing
{
    public class BoardController : MonoBehaviour
    {
        [SerializeField] private GameObject _tilePrefab;
        [SerializeField] private GameObject _npcCubePrefab;
        [SerializeField] private float      _tileSpacing = 2.2f;

        private NpcConfig _npcConfig;

        // Tile positions indexed 1–20
        private readonly Vector3[] _positions = new Vector3[21];

        public Dictionary<int, NpcCubeController> NpcCubes { get; } = new();

        [Inject]
        public void Construct(NpcConfig npcConfig) => _npcConfig = npcConfig;

        private void Awake()
        {
            BuildPositions();
            SpawnTiles();
            SpawnNpcCubes();
        }

        // Snake layout: 5 columns × 4 rows (indices 1-20)
        // Row 0 (z=0): 1→2→3→4→5 (left to right)
        // Row 1 (z=1): 6→7→8→9→10 (right to left)
        // Row 2 (z=2): 11→12→13→14→15 (left to right)
        // Row 3 (z=3): 16→17→18→19→20 (right to left)
        private void BuildPositions()
        {
            float s = _tileSpacing;
            int cols = 5;
            for (int i = 1; i <= 20; i++)
            {
                int row = (i - 1) / cols;
                int col = (i - 1) % cols;
                float x = (row % 2 == 0) ? col * s : (cols - 1 - col) * s;
                float z = row * s;
                _positions[i] = new Vector3(x, 0f, z);
            }
        }

        public Vector3 GetSquarePosition(int squareIndex)
        {
            if (squareIndex < 1 || squareIndex > 20) return _positions[1];
            return _positions[squareIndex];
        }

        private void SpawnTiles()
        {
            for (int i = 1; i <= 20; i++)
            {
                var tile = Instantiate(_tilePrefab, _positions[i], Quaternion.identity, transform);
                tile.name = $"Tile_{i:D2}";

                // Label tile number
                var label = tile.GetComponentInChildren<TMPro.TMP_Text>();
                if (label != null) label.text = i.ToString();

                // Color start/finish differently
                var renderer = tile.GetComponent<Renderer>();
                if (renderer != null)
                {
                    if (i == 1)  renderer.material.color = new Color(0.4f, 0.8f, 0.4f); // green = start
                    if (i == 20) renderer.material.color = new Color(0.9f, 0.7f, 0.2f); // gold = finish
                    else if (i % 2 == 0) renderer.material.color = new Color(0.85f, 0.85f, 0.85f);
                }
            }
        }

        private void SpawnNpcCubes()
        {
            Vector3 startPos = _positions[1];
            int count = _npcConfig.npcs.Length;

            for (int i = 0; i < count; i++)
            {
                var entry  = _npcConfig.npcs[i];
                // Offset slightly so stacked cubes are visible
                Vector3 offset = new Vector3(i * 0.25f - (count - 1) * 0.125f, i * 0.5f + 0.5f, 0f);
                var cube = Instantiate(_npcCubePrefab, startPos + offset, Quaternion.identity, transform);
                cube.name = $"Npc_{entry.id}";

                var ctrl = cube.GetComponent<NpcCubeController>();
                ctrl.Initialize(entry.id, entry.color);
                NpcCubes[entry.id] = ctrl;
            }
        }
    }
}
```

- [ ] **Step 3: Create NpcCubeController.cs**

Create `Assets/Scripts/Race/NpcCubeController.cs`:

```csharp
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

namespace CubeRacing
{
    public class NpcCubeController : MonoBehaviour
    {
        private int   _npcId;
        private int   _stackIndex; // vertical offset for stacking

        public void Initialize(int npcId, Color color)
        {
            _npcId = npcId;
            var renderer = GetComponent<Renderer>();
            if (renderer != null) renderer.material.color = color;
        }

        public async UniTask MoveToAsync(Vector3 target, float duration)
        {
            // Hop arc: move up then forward then down
            var mid = (transform.position + target) * 0.5f + Vector3.up * 1.2f;
            await transform.DOPath(new[] { mid, target }, duration, PathType.CatmullRom)
                           .SetEase(Ease.InOutSine)
                           .ToUniTask();
        }

        public void SetStackOffset(int index)
        {
            _stackIndex = index;
            var pos = transform.position;
            transform.position = new Vector3(pos.x, 0.5f + index * 0.5f, pos.z);
        }
    }
}
```

- [ ] **Step 4: Create BoardTile.prefab**

In Unity Editor:
1. Create a new GameObject → 3D Object → Cube. Scale to `(2f, 0.2f, 2f)`.
2. Add a TMP_Text child (Canvas in World Space, very small, facing up) for the tile number.
3. Save as `Assets/Prefabs/BoardTile.prefab`.

- [ ] **Step 5: Create NpcCube.prefab**

1. Create a new GameObject → 3D Object → Cube. Scale to `(0.8f, 0.8f, 0.8f)`.
2. Attach `NpcCubeController` component.
3. Save as `Assets/Prefabs/NpcCube.prefab`.

- [ ] **Step 6: Build RaceScene in Unity Editor**

New scene, save as `Assets/Scenes/RaceScene.unity`. Add to Build Settings.

```
RaceScene
├── Main (DontDestroyOnLoad LifetimeScope)
├── RaceScope (RaceScope component; Parent → Main)
├── Lighting
│   ├── Directional Light (rotation 50°, -30°, 0°)
│   └── (adjust ambient if needed)
├── Board (empty GameObject, add BoardController)
│   ├── _tilePrefab → BoardTile prefab
│   └── _npcCubePrefab → NpcCube prefab
├── Camera (position: (4.4, 14, -5), rotation: (60, 0, 0))
│   └── Camera component (Field of View: 45)
└── EventSystem
```

Add `RacePresenter` GameObject (empty, with RacePresenter component — created in Task 11).

- [ ] **Step 7: Manual test — board layout**

Enter Play mode in RaceScene. Verify:
- 20 tiles appear in snake pattern: 5 wide × 4 rows
- Tile 1 (Start) is green, Tile 20 (Finish) is gold
- 4 coloured cubes spawn at Tile 1 position, slightly offset/stacked
- Camera angle shows the full board

- [ ] **Step 8: Commit**

```bash
git add frontend/unity/Assets/Scripts/Race/RaceScope.cs \
        frontend/unity/Assets/Scripts/Race/BoardController.cs \
        frontend/unity/Assets/Scripts/Race/NpcCubeController.cs \
        frontend/unity/Assets/Scenes/RaceScene.unity \
        frontend/unity/Assets/Prefabs/BoardTile.prefab \
        frontend/unity/Assets/Prefabs/NpcCube.prefab
git commit -m "feat: add RaceScene with 2.5D snake board and NPC cubes"
```

---

## Task 11: RacePresenter + Animation

**Files:**
- Create: `Assets/Scripts/Race/RacePresenter.cs`

**Interfaces:**
- Consumes: `BoardController`, `GameStateService`, `PlayerSession`, `NpcConfig`; `ISubscriber<RoundExecutedMessage>`, `ISubscriber<RaceCompletedMessage>`, `ISubscriber<SettlementDoneMessage>`
- Produces: Full race animation and settlement UI

- [ ] **Step 1: Create RacePresenter.cs**

Create `Assets/Scripts/Race/RacePresenter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using MessagePipe;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VContainer;

namespace CubeRacing
{
    public class RacePresenter : MonoBehaviour
    {
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private TMP_Text _chipsText;
        [SerializeField] private Button   _backButton;
        [SerializeField] private TMP_Text _backButtonText;

        // Settlement panel
        [SerializeField] private GameObject _settlementPanel;
        [SerializeField] private TMP_Text   _settlementText;
        [SerializeField] private Button     _returnButton;

        // Winner banner
        [SerializeField] private GameObject _winnerBanner;
        [SerializeField] private TMP_Text   _winnerText;

        private BoardController  _board;
        private GameStateService _gameState;
        private PlayerSession    _session;
        private NpcConfig        _npcConfig;
        private ISubscriber<RoundExecutedMessage>  _roundSubscriber;
        private ISubscriber<RaceCompletedMessage>  _raceCompletedSubscriber;
        private ISubscriber<SettlementDoneMessage> _settlementSubscriber;

        private readonly CompositeDisposable _disposables = new();
        private bool _animating = false;

        [Inject]
        public void Construct(
            BoardController board, GameStateService gameState,
            PlayerSession session, NpcConfig npcConfig,
            ISubscriber<RoundExecutedMessage>  roundSubscriber,
            ISubscriber<RaceCompletedMessage>  raceCompletedSubscriber,
            ISubscriber<SettlementDoneMessage> settlementSubscriber)
        {
            _board                   = board;
            _gameState               = gameState;
            _session                 = session;
            _npcConfig               = npcConfig;
            _roundSubscriber         = roundSubscriber;
            _raceCompletedSubscriber = raceCompletedSubscriber;
            _settlementSubscriber    = settlementSubscriber;
        }

        private void Start()
        {
            _settlementPanel.SetActive(false);
            _winnerBanner.SetActive(false);
            _backButton.interactable = false;
            _backButton.onClick.AddListener(ReturnToLobby);
            _returnButton.onClick.AddListener(ReturnToLobby);

            _session.Chips.Subscribe(c => _chipsText.text = $"籌碼：{c:N0}").AddTo(_disposables);
            _gameState.Status.Subscribe(s => _statusText.text = StatusToText(s)).AddTo(_disposables);

            _roundSubscriber.Subscribe(m =>
                PlayRoundAsync(m.Payload, destroyCancellationToken).Forget()).AddTo(_disposables);

            _raceCompletedSubscriber.Subscribe(m =>
                ShowWinnerAsync(m.WinnerNpcId, destroyCancellationToken).Forget()).AddTo(_disposables);

            _settlementSubscriber.Subscribe(m =>
                ShowSettlement(m.Payload)).AddTo(_disposables);
        }

        private async UniTaskVoid PlayRoundAsync(RoundExecutedPayload payload, CancellationToken ct)
        {
            if (payload.actions == null || payload.actions.Count == 0) return;
            if (_animating) return;
            _animating = true;

            float durationPerAction = (GameSettings.RoundIntervalMs / 1000f * 0.8f)
                                      / payload.actions.Count;

            foreach (var action in payload.actions)
            {
                var targetPos = _board.GetSquarePosition(action.toSquare);

                // Move leader NPC
                if (_board.NpcCubes.TryGetValue(action.npcId, out var cube))
                    await cube.MoveToAsync(targetPos, durationPerAction);

                // Move carried NPCs to same target
                foreach (var carriedId in action.carriedNpcIds)
                {
                    if (_board.NpcCubes.TryGetValue(carriedId, out var carried))
                        carried.transform.position = targetPos + Vector3.up * 0.5f;
                }

                await UniTask.Delay(50, cancellationToken: ct); // brief pause between actions
            }

            _animating = false;
        }

        private async UniTaskVoid ShowWinnerAsync(int winnerNpcId, CancellationToken ct)
        {
            var entry = _npcConfig.GetById(winnerNpcId);
            _winnerText.text = $"{entry?.npcName ?? winnerNpcId.ToString()} 獲勝！";
            _winnerBanner.SetActive(true);

            // Punch scale on winner cube
            if (_board.NpcCubes.TryGetValue(winnerNpcId, out var cube))
                cube.transform.DOPunchScale(Vector3.one * 0.5f, 0.6f, 5);

            await UniTask.Delay(1500, cancellationToken: ct);
        }

        private void ShowSettlement(SettlementDonePayload payload)
        {
            var me = payload.playerResults?.Find(r => r.nickname == _session.Nickname);
            if (me != null)
            {
                _session.UpdateChips(_session.Chips.CurrentValue + me.winAmount);
                _settlementText.text = me.winAmount > 0
                    ? $"🎉 恭喜！獲得 +{me.winAmount:N0} 籌碼"
                    : "本局未中獎";
            }
            else
            {
                _settlementText.text = "未參與本局";
            }

            _settlementPanel.SetActive(true);
            _backButton.interactable = true;
        }

        private void ReturnToLobby()
            => SceneManager.LoadSceneAsync("LobbyScene")
                           .ToUniTask(cancellationToken: destroyCancellationToken).Forget();

        private static string StatusToText(string status) => status switch
        {
            "Racing"   => "比賽進行中",
            "Settling" => "結算中...",
            "Completed" => "比賽結束",
            _          => status
        };

        private void OnDestroy() => _disposables.Dispose();
    }

    // Expose RoundIntervalMs as a static constant so RacePresenter can use it
    // without referencing the backend's GameSettings class.
    internal static class GameSettings
    {
        public const int RoundIntervalMs = 1500;
    }
}
```

- [ ] **Step 2: Build RacePresenter UI in RaceScene**

Add to RaceScene Canvas:

```
Canvas (Screen Space - Overlay)
├── HUD (anchored top)
│   ├── BackButton (top-left, disabled at start → _backButton, text "← 返回大廳")
│   ├── StatusText (top-center → _statusText)
│   └── ChipsText (top-right → _chipsText)
├── WinnerBanner (center, hidden by default → _winnerBanner)
│   └── WinnerText (TMP_Text → _winnerText)
└── SettlementPanel (center, ~360×240, hidden → _settlementPanel)
    ├── SettlementText (TMP_Text → _settlementText)
    └── ReturnButton ("返回大廳" → _returnButton)
```

Wire `RacePresenter` component fields. Add EventSystem.

- [ ] **Step 3: Wire RacePresenter in LobbyScope** 

In `LobbyScope.Configure()`, ensure `RaceScope` is set as parent of LobbyScope so that the race scene picks up the ProjectScope services. (Both scenes should find the `Main` ProjectScope automatically since it's DontDestroyOnLoad.)

- [ ] **Step 4: End-to-end manual test**

Start backend. Enter Play mode at LoginScene:
1. Login → reaches LobbyScene
2. Wait for Betting phase → place a bet on one NPC
3. Click 觀看比賽 → RaceScene loads
4. Watch 4 cubes move sequentially per `RoundExecuted` with hop arc
5. Winner cube punches scale + banner appears
6. Settlement panel shows win/loss amount
7. Click 返回大廳 → back to LobbyScene, chips updated

- [ ] **Step 5: Commit**

```bash
git add frontend/unity/Assets/Scripts/Race/RacePresenter.cs \
        frontend/unity/Assets/Scenes/RaceScene.unity
git commit -m "feat: add RacePresenter with sequential animation, winner reveal, and settlement"
```

---

## Spec Coverage Check

| Spec Requirement | Task |
|---|---|
| Token saved to PlayerPrefs | Task 2 (PlayerSession) |
| Nickname pre-filled on return | Task 6 (LoginPresenter) |
| Buttons disabled during API calls | Tasks 6, 8 (SetLoading) |
| Status bar text per phase | Task 7 (LobbyPresenter) |
| NPC cards with default 1.3x odds | Task 7 (NpcCardView) |
| Betting disabled outside Betting phase | Task 7 (OnStatusChanged) |
| HasPlacedBet disables all bet buttons | Task 7 (OnBetPlacedChanged) |
| Fixed denomination betting dialog | Task 8 |
| API error shown in dialog (no close) | Task 8 (ConfirmAsync) |
| Watch Race button disabled/enabled | Task 7 (OnStatusChanged) |
| Leaderboard top-20 with self-highlight | Task 9 |
| SignalR auto ping-pong | Task 4 (ProcessMessage type=6) |
| 2.5D snake board 5×4 | Task 10 (BuildPositions) |
| NPC colour from NpcConfig | Tasks 5, 10 |
| Sequential per-action animation | Task 11 (PlayRoundAsync) |
| CarriedNpcIds move with leader | Task 11 (PlayRoundAsync) |
| Winner punch scale + banner | Task 11 (ShowWinnerAsync) |
| Settlement panel + chips update | Task 11 (ShowSettlement) |
| Return to Lobby after settlement | Task 11 (ReturnToLobby) |
