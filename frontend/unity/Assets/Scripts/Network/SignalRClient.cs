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
