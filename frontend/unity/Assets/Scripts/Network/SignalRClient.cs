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
        private readonly GameStateService                   _gameState;
        private readonly IPublisher<BettingStartedMessage>  _bettingStartedPublisher;
        private readonly IPublisher<RaceStartingMessage>    _raceStartingPublisher;
        private readonly IPublisher<OddsUpdatedMessage>     _oddsPublisher;
        private readonly IPublisher<BettingEndedMessage>    _bettingEndedPublisher;
        private readonly IPublisher<RoundExecutedMessage>   _roundPublisher;
        private readonly IPublisher<RaceCompletedMessage>   _raceCompletedPublisher;
        private readonly IPublisher<SettlementDoneMessage>  _settlementPublisher;

        private ClientWebSocket          _ws;
        private CancellationTokenSource  _cts;
        private string                   _lastSessionId;

        public SignalRClient(
            string url,
            GameStateService gameState,
            IPublisher<BettingStartedMessage>  bettingStartedPublisher,
            IPublisher<RaceStartingMessage>    raceStartingPublisher,
            IPublisher<OddsUpdatedMessage>     oddsPublisher,
            IPublisher<BettingEndedMessage>    bettingEndedPublisher,
            IPublisher<RoundExecutedMessage>   roundPublisher,
            IPublisher<RaceCompletedMessage>   raceCompletedPublisher,
            IPublisher<SettlementDoneMessage>  settlementPublisher)
        {
            _url                     = url;
            _gameState               = gameState;
            _bettingStartedPublisher = bettingStartedPublisher;
            _raceStartingPublisher   = raceStartingPublisher;
            _oddsPublisher           = oddsPublisher;
            _bettingEndedPublisher   = bettingEndedPublisher;
            _roundPublisher          = roundPublisher;
            _raceCompletedPublisher  = raceCompletedPublisher;
            _settlementPublisher     = settlementPublisher;
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
            _lastSessionId = sessionId;
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
            while (!ct.IsCancellationRequested)
            {
                while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
                {
                    try
                    {
                        var message = await ReceiveMessageAsync(ct);
                        if (!string.IsNullOrWhiteSpace(message))
                            ProcessMessage(message);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[SignalR] Receive error: {e.Message}");
                        break;
                    }
                }

                if (ct.IsCancellationRequested) return;

                // Disconnected — attempt reconnect
                _gameState.IsConnected.Value = false;
                Debug.Log("[SignalR] Disconnected. Attempting reconnect...");
                await UniTask.Delay(3000, cancellationToken: ct);

                try
                {
                    _ws?.Dispose();
                    _ws = new ClientWebSocket();
                    await _ws.ConnectAsync(new Uri(_url), ct);
                    await SendRawAsync($"{{\"protocol\":\"json\",\"version\":1}}{Separator}", ct);
                    await ReceiveMessageAsync(ct); // discard handshake response
                    if (!string.IsNullOrEmpty(_lastSessionId))
                        await JoinSessionAsync(_lastSessionId, ct);
                    _gameState.IsConnected.Value = true;
                    Debug.Log("[SignalR] Reconnected.");
                }
                catch (OperationCanceledException) { return; }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SignalR] Reconnect failed: {e.Message}");
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

                        switch (target)
                        {
                            case "BettingStarted":
                                _bettingStartedPublisher.Publish(new BettingStartedMessage());
                                break;
                            case "RaceStarting":
                                if (args?.Count > 0)
                                {
                                    var raceStartsAt = args[0]["raceStartsAt"].Value<DateTime>();
                                    _raceStartingPublisher.Publish(new RaceStartingMessage(raceStartsAt));
                                }
                                break;
                            case "OddsUpdated":
                                if (args?.Count > 0)
                                    _oddsPublisher.Publish(new OddsUpdatedMessage(
                                        args[0].ToObject<System.Collections.Generic.List<NpcOddsDto>>()));
                                break;
                            case "BettingEnded":
                                _bettingEndedPublisher.Publish(new BettingEndedMessage());
                                break;
                            case "RoundExecuted":
                                if (args?.Count > 0)
                                    _roundPublisher.Publish(new RoundExecutedMessage(
                                        args[0].ToObject<RoundExecutedPayload>()));
                                break;
                            case "RaceCompleted":
                                if (args?.Count > 0)
                                    _raceCompletedPublisher.Publish(new RaceCompletedMessage(
                                        args[0]["winnerNpcId"].Value<int>()));
                                break;
                            case "SettlementDone":
                                if (args?.Count > 0)
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
