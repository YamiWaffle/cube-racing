using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace CubeRacing
{
    public class LobbyPresenter : MonoBehaviour
    {
        [SerializeField] private TMP_Text                _chipsText;
        [SerializeField] private TMP_Text                _statusText;
        [SerializeField] private Button                  _watchRaceButton;
        [SerializeField] private TMP_Text                _watchRaceButtonText;
        [SerializeField] private Button                  _leaderboardButton;
        [SerializeField] private Transform               _npcCardsContainer;
        [SerializeField] private NpcCardView             _npcCardPrefab;
        [SerializeField] private BettingDialogPresenter  _bettingDialog;
        [SerializeField] private LeaderboardPresenter    _leaderboardPanel;

        private bool _isStarted;
        private bool _isInjected;
        private bool _isInitialized;
        private PlayerSession    _session;
        private ApiClient        _api;
        private SignalRClient    _signalR;
        private GameStateService _gameState;
        private NpcConfig        _npcConfig;
        private ISubscriber<BettingStartedMessage>  _bettingStartedSubscriber;
        private ISubscriber<SettlementDoneMessage>  _settlementSubscriber;
        private SceneLoader      _sceneLoader;

        // Key: npcId — fixes the index-based lookup bug from the plan
        private readonly Dictionary<int, NpcCardView> _cards        = new();
        private readonly CompositeDisposable           _disposables  = new();
        private CancellationTokenSource                _countdownCts;

        [Inject]
        public void Construct(
            PlayerSession session, ApiClient api, SignalRClient signalR,
            GameStateService gameState, NpcConfig npcConfig,
            ISubscriber<BettingStartedMessage>  bettingStartedSubscriber,
            ISubscriber<SettlementDoneMessage>  settlementSubscriber,
            SceneLoader sceneLoader)
        {
            _session                  = session;
            _api                      = api;
            _signalR                  = signalR;
            _gameState                = gameState;
            _npcConfig                = npcConfig;
            _bettingStartedSubscriber = bettingStartedSubscriber;
            _settlementSubscriber     = settlementSubscriber;
            _sceneLoader              = sceneLoader;

            _isInjected = true;

            InitAsync(destroyCancellationToken).Forget();
        }

        private void Start()
        {
            _watchRaceButton.onClick.AddListener(OnWatchRaceClicked);
            _leaderboardButton.onClick.AddListener(() => _leaderboardPanel.Show(destroyCancellationToken).Forget());

            // Use _session.Chips (PlayerSession owns chip balance, not GameStateService)
            _session.Chips.Subscribe(c => _chipsText.text = $"Chips: {c:N0}").AddTo(_disposables);
            _gameState.Status.Subscribe(OnStatusChanged).AddTo(_disposables);
            _gameState.NpcOdds.Subscribe(OnOddsChanged).AddTo(_disposables);
            _gameState.HasPlacedBet.Subscribe(OnBetPlacedChanged).AddTo(_disposables);
            _settlementSubscriber.Subscribe(OnSettlementDone).AddTo(_disposables);

            // Re-sync when a new betting round starts (backend broadcasts BettingStarted to all clients)
            _bettingStartedSubscriber
                .Subscribe(_ => ResyncSessionAsync(destroyCancellationToken).Forget())
                .AddTo(_disposables);

            // Re-sync after SignalR reconnect (may have missed events during disconnect)
            _gameState.IsConnected
                .Where(v => v)
                .Skip(1)
                .Subscribe(_ => ResyncSessionAsync(destroyCancellationToken).Forget())
                .AddTo(_disposables);

            _isStarted = true;

            InitAsync(destroyCancellationToken).Forget();
        }

        private async UniTaskVoid InitAsync(CancellationToken ct)
        {
            if (_isInitialized || !_isInjected || !_isStarted)
                return;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var session = await _api.GetCurrentSessionAsync(ct);
                    _gameState.ApplySession(session);
                    SpawnNpcCards(session.npcOdds);

                    // Poll until session leaves Waiting phase — no SignalR events during Waiting
                    while (!ct.IsCancellationRequested && session.status == "Waiting")
                    {
                        await UniTask.Delay(3000, cancellationToken: ct);
                        session = await _api.GetCurrentSessionAsync(ct);
                        _gameState.ApplySession(session);
                    }
                    if (ct.IsCancellationRequested) return;

                    await _signalR.ConnectAsync(ct);
                    await _signalR.JoinSessionAsync(session.sessionId.ToString(), ct);
                    _gameState.IsConnected.Value = true;
                    _isInitialized = true;
                    return;
                }
                catch (ApiException ex) when (ex.StatusCode == 401)
                {
                    PlayerPrefs.DeleteKey("player_token");
                    PlayerPrefs.DeleteKey("player_nickname");
                    PlayerPrefs.DeleteKey("player_chips");
                    PlayerPrefs.Save();
                    _sceneLoader.LoadAsync("LoginScene").Forget();
                    return;
                }
                catch (ApiException ex) when (ex.StatusCode == 404)
                {
                    _statusText.text = "Waiting for session...";
                    await UniTask.Delay(5000, cancellationToken: ct);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception e)
                {
                    _statusText.text = $"Connection failed: {e.Message}";
                    return;
                }
            }
        }

        private async UniTaskVoid ResyncSessionAsync(CancellationToken ct)
        {
            try
            {
                var session = await _api.GetCurrentSessionAsync(ct);
                _gameState.ApplySession(session);
                SpawnNpcCards(session.npcOdds);
                await _signalR.JoinSessionAsync(session.sessionId.ToString(), ct);
            }
            catch (ApiException ex) when (ex.StatusCode == 404) { }
            catch (OperationCanceledException) { }
            catch (Exception e) { Debug.LogWarning($"[Lobby] Resync failed: {e.Message}"); }
        }

        private void SpawnNpcCards(List<NpcOddsDto> odds)
        {
            foreach (Transform child in _npcCardsContainer) Destroy(child.gameObject);
            _cards.Clear();

            bool canBet = _gameState.Status.CurrentValue == "Betting"
                          && !_gameState.HasPlacedBet.CurrentValue;
            foreach (var entry in _npcConfig.npcs)
            {
                var card    = Instantiate(_npcCardPrefab, _npcCardsContainer);
                var npcOdds = odds?.Find(o => o.npcId == entry.id);
                card.SetNpc(entry, npcOdds?.odds ?? 1.3);
                card.SetBettingEnabled(canBet);
                card.OnBetClicked += npcId => _bettingDialog.Show(npcId, destroyCancellationToken).Forget();
                _cards[entry.id] = card;
            }
        }

        private void OnStatusChanged(string status)
        {
            _statusText.text = status switch
            {
                "Waiting"   => "Preparing...",
                "Betting"   => $"Betting closes in {_gameState.SecondsRemaining.Value ?? 0}s",
                "Racing"    => "Race in progress",
                "Settling"  => "Settling...",
                "Completed" => $"{GetWinnerName()} wins!",
                _           => status
            };

            bool isBetting = status == "Betting";
            bool canWatch  = status is "Racing" or "Settling" or "Completed";

            foreach (var card in _cards.Values)
                card.SetBettingEnabled(isBetting && !_gameState.HasPlacedBet.CurrentValue);

            _watchRaceButton.interactable = canWatch;
            _watchRaceButtonText.text     = canWatch ? "Watch Race" : "Not started";

            if (isBetting) StartCountdown();
            else StopCountdown();
        }

        private void OnOddsChanged(List<NpcOddsDto> odds)
        {
            if (odds == null) return;
            foreach (var o in odds)
            {
                if (_cards.TryGetValue(o.npcId, out var card))
                    card.UpdateOdds(o.odds);
            }
        }

        private void OnBetPlacedChanged(bool placed)
        {
            if (!placed) return;
            foreach (var card in _cards.Values) card.SetBetPlaced(true);
        }

        private void OnSettlementDone(SettlementDoneMessage msg)
        {
            var me = msg.Payload.playerResults?.Find(r => r.nickname == _session.Nickname);
            if (me != null) _session.UpdateChips(_session.Chips.CurrentValue + me.winAmount);
        }

        private void OnWatchRaceClicked()
            => _sceneLoader.LoadAsync("RaceScene").Forget();

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
                _statusText.text = $"Betting closes in {_gameState.SecondsRemaining.Value}s";
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
