using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using MessagePipe;
using R3;
using TMPro;
using UnityEngine;
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

        // Winner text — child of _settlementPanel, wired in Inspector
        [SerializeField] private TMP_Text _winnerText;

        // Round presentation
        [SerializeField] private RoundToastView    _roundToast;
        [SerializeField] private DiceRollPanelView _dicePanel;
        [SerializeField] private RoundBottomHud    _bottomHud;

        private BoardController  _board;
        private GameStateService _gameState;
        private PlayerSession    _session;
        private NpcConfig        _npcConfig;
        private ApiClient        _api;
        private RaceConfig       _raceConfig;
        private RaceCameraController _camera;
        private ISubscriber<RoundExecutedMessage>  _roundSubscriber;
        private ISubscriber<RaceCompletedMessage>  _raceCompletedSubscriber;
        private ISubscriber<SettlementDoneMessage> _settlementSubscriber;
        private SceneLoader      _sceneLoader;

        private readonly CompositeDisposable          _disposables = new();
        private readonly Queue<RoundExecutedPayload> _roundQueue  = new();
        private readonly Dictionary<int, List<int>> _localStacks = new();
        private bool _animating = false;
        private UniTask _initialSyncTask = UniTask.CompletedTask;
        private CancellationTokenSource _countdownCts;
        private int? _pendingWinnerNpcId;
        private SettlementDonePayload _pendingSettlement;
        private bool _raceOver = false;

        [Inject]
        public void Construct(
            BoardController board, GameStateService gameState,
            PlayerSession session, NpcConfig npcConfig, ApiClient api,
            RaceConfig raceConfig,
            RaceCameraController camera,
            ISubscriber<RoundExecutedMessage>  roundSubscriber,
            ISubscriber<RaceCompletedMessage>  raceCompletedSubscriber,
            ISubscriber<SettlementDoneMessage> settlementSubscriber,
            SceneLoader sceneLoader)
        {
            _board                   = board;
            _gameState               = gameState;
            _session                 = session;
            _npcConfig               = npcConfig;
            _api                     = api;
            _raceConfig              = raceConfig;
            _camera                  = camera;
            _roundSubscriber         = roundSubscriber;
            _raceCompletedSubscriber = raceCompletedSubscriber;
            _settlementSubscriber    = settlementSubscriber;
            _sceneLoader             = sceneLoader;
        }

        private void Start()
        {
            _settlementPanel.SetActive(false);
            _backButton.interactable = true;
            _backButton.onClick.AddListener(ReturnToLobby);
            _bottomHud.Initialize(_npcConfig);
            _returnButton.onClick.AddListener(ReturnToLobby);

            _session.Chips.Subscribe(c => _chipsText.text = $"Chips: {c:N0}").AddTo(_disposables);
            _gameState.Status.Subscribe(s => _statusText.text = StatusToText(s)).AddTo(_disposables);

            _roundSubscriber.Subscribe(m =>
            {
                _roundQueue.Enqueue(m.Payload);
                if (!_animating)
                    DrainQueueAsync(destroyCancellationToken).Forget();
            }).AddTo(_disposables);

            _raceCompletedSubscriber.Subscribe(m =>
            {
                if (!_animating)
                {
                    ShowResult(m.WinnerNpcId, _pendingSettlement);
                    _pendingSettlement = null;
                }
                else
                {
                    _pendingWinnerNpcId = m.WinnerNpcId;
                }
            }).AddTo(_disposables);

            _settlementSubscriber.Subscribe(m =>
            {
                if (_animating)
                {
                    _pendingSettlement = m.Payload;
                }
                else if (_settlementPanel.activeSelf)
                {
                    // Panel already showing "結算中..." — fill in settlement text now
                    ApplySettlementText(m.Payload);
                }
                else
                {
                    _pendingSettlement = m.Payload;
                }
            }).AddTo(_disposables);

            _initialSyncTask = SyncNpcPositionsAsync(destroyCancellationToken);

            _gameState.RaceStartsAt
                .Where(t => t.HasValue && t.Value > DateTime.UtcNow)
                .Subscribe(t => StartCountdown(t!.Value))
                .AddTo(_disposables);
        }

        private async UniTask SyncNpcPositionsAsync(CancellationToken ct)
        {
            try
            {
                var stacks = await _api.GetCurrentSquaresAsync(ct);
                ApplySquareStacks(stacks);

                if (_gameState.RaceStartsAt.Value == null)
                {
                    var session = await _api.GetCurrentSessionAsync(ct);
                    if (session != null
                        && session.raceStartsAt.HasValue
                        && session.raceStartsAt.Value > DateTime.UtcNow)
                    {
                        _gameState.RaceStartsAt.Value = session.raceStartsAt.Value;
                    }
                }
            }
            catch (ApiException ex) when (ex.StatusCode == 204 || ex.StatusCode == 404) { }
            catch (OperationCanceledException) { }
            catch (Exception e) { Debug.LogWarning($"[Race] Sync failed: {e.Message}"); }
        }

        private void StartCountdown(DateTime raceStartsAt)
        {
            _countdownCts?.Cancel();
            _countdownCts?.Dispose();
            _countdownCts = new CancellationTokenSource();
            CountdownAsync(raceStartsAt, _countdownCts.Token).Forget();
        }

        private async UniTaskVoid CountdownAsync(DateTime raceStartsAt, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && DateTime.UtcNow < raceStartsAt)
            {
                int remaining = Math.Max(0, (int)(raceStartsAt - DateTime.UtcNow).TotalSeconds);
                _statusText.text = $"The racing will begin in {remaining} seconds....";
                await UniTask.Delay(1000, cancellationToken: ct);
            }
            // Status text reverts to GameStateService.Status subscription once countdown ends
        }

        private void ApplySquareStacks(Dictionary<string, List<int>> stacks)
        {
            if (stacks == null) return;

            foreach (var cube in _board.NpcCubes.Values)
                cube.transform.SetParent(null);

            _localStacks.Clear();

            foreach (var (squareStr, npcIds) in stacks)
            {
                if (!int.TryParse(squareStr, out int sq)) continue;
                _localStacks[sq] = new List<int>(npcIds);
                var sqPos = _board.GetSquarePosition(sq);

                for (int i = 0; i < npcIds.Count; i++)
                {
                    if (!_board.NpcCubes.TryGetValue(npcIds[i], out var cube)) continue;
                    if (i == 0)
                    {
                        cube.transform.SetParent(null);
                        cube.transform.position = sqPos + Vector3.up * _raceConfig.npcHeight;
                    }
                    else
                    {
                        if (!_board.NpcCubes.TryGetValue(npcIds[i - 1], out var below)) continue;
                        cube.transform.SetParent(below.transform);
                        cube.transform.localPosition = Vector3.up * _raceConfig.npcHeight;
                    }
                }
            }
        }

        private async UniTaskVoid DrainQueueAsync(CancellationToken ct)
        {
            _raceOver = false;
            _animating = true;
            await _initialSyncTask;
            try
            {
                while (_roundQueue.Count > 0 && !ct.IsCancellationRequested && !_raceOver)
                {
                    var payload = _roundQueue.Dequeue();
                    await PlayRoundAsync(payload, ct);
                }
            }
            finally
            {
                _animating = false;
                if (_pendingWinnerNpcId.HasValue)
                {
                    ShowResult(_pendingWinnerNpcId.Value, _pendingSettlement);
                    _pendingWinnerNpcId = null;
                    _pendingSettlement   = null;
                }
            }
        }

        private async UniTask PlayRoundAsync(RoundExecutedPayload payload, CancellationToken ct)
        {
            if (payload.actions == null || payload.actions.Count == 0) return;

            var npcOrder = payload.actions.Select(a => a.npcId).ToArray();
            var npcDice = payload.actions.ToDictionary(
                action => action.npcId,
                action => action.diceRoll);

            _statusText.text = $"Round {payload.roundNumber}";
            await _roundToast.ShowAsync(payload.roundNumber, ct);
            await _dicePanel.ShowAsync(_npcConfig, npcOrder, npcDice, ct);
            _bottomHud.SetRound(npcOrder, npcDice);

            // --- Debug logging ---
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[Race] Round {payload.roundNumber} — {payload.actions.Count} action(s):");
            foreach (var a in payload.actions)
            {
                var npcName = _npcConfig.GetById(a.npcId)?.npcName ?? $"NPC {a.npcId}";
                var carried = a.carriedNpcIds.Count > 0 ? $" carries=[{string.Join(",", a.carriedNpcIds)}]" : "";
                sb.AppendLine($"  {npcName}: from={a.fromSquare} to={a.toSquare} steps={a.toSquare - a.fromSquare} dice={a.diceRoll}{carried}");
            }
            Debug.Log(sb.ToString());
            // ---------------------

            bool hadValidationError = false;

            foreach (var action in payload.actions)
            {
                int steps = action.toSquare - action.fromSquare;
                if (steps <= 0) continue;

                if (!_board.NpcCubes.TryGetValue(action.npcId, out var movingCube)) continue;

                var npcName = _npcConfig.GetById(action.npcId)?.npcName ?? $"NPC {action.npcId}";
                _statusText.text = $"Round {payload.roundNumber} - {npcName} run!";
                _bottomHud.SetActiveNpc(action.npcId);
                await _camera.FocusOnAsync(movingCube.transform, ct);

                if (_localStacks.TryGetValue(action.fromSquare, out var fromList))
                {
                    fromList.Remove(action.npcId);
                    foreach (var cId in action.carriedNpcIds)
                        fromList.Remove(cId);
                }

                movingCube.transform.SetParent(null);

                Debug.Log($"[Race] R{payload.roundNumber} {npcName}: animating {steps} step(s), sq {action.fromSquare}→{action.toSquare}");

                for (int sq = action.fromSquare + 1; sq <= action.toSquare; sq++)
                {
                    int   stackCount  = _localStacks.TryGetValue(sq, out var existing) ? existing.Count : 0;
                    float h           = _raceConfig.npcHeight;
                    var   targetWorld = _board.GetSquarePosition(sq) + Vector3.up * (h + stackCount * h);

                    Debug.Log($"[Race] R{payload.roundNumber} {npcName}: step to sq={sq} stackCount={stackCount} target={targetWorld}");
                    await movingCube.MoveToAsync(targetWorld, _raceConfig.stepDuration, ct);
                    Debug.Log($"[Race] R{payload.roundNumber} {npcName}: arrived sq={sq} worldPos={movingCube.transform.position}");

                    if (stackCount > 0 && _board.NpcCubes.TryGetValue(_localStacks[sq][^1], out var topNpc))
                        movingCube.transform.SetParent(topNpc.transform);

                    if (sq != action.toSquare)
                        movingCube.transform.SetParent(null);
                }

                if (!_localStacks.TryGetValue(action.toSquare, out var toList))
                    _localStacks[action.toSquare] = toList = new List<int>();
                toList.Add(action.npcId);
                toList.AddRange(action.carriedNpcIds);

                var actualDescendants = GetAllDescendantIds(action.npcId);
                var expectedSet       = new HashSet<int>(action.carriedNpcIds);
                var actualSet         = new HashSet<int>(actualDescendants);
                if (!expectedSet.SetEquals(actualSet))
                {
                    Debug.LogError($"[Race] Stack mismatch NPC {action.npcId}. " +
                                   $"Expected: [{string.Join(",", expectedSet)}] " +
                                   $"Actual: [{string.Join(",", actualSet)}]");
                    hadValidationError = true;
                }

                if (action.toSquare == _gameState.MapLength - 1)
                {
                    _raceOver = true;
                    break;
                }
            }

            _bottomHud.Hide();

            if (_raceOver) return;

            _localStacks.Clear();
            foreach (var (k, v) in payload.squareStacks)
                if (int.TryParse(k, out int sq))
                    _localStacks[sq] = new List<int>(v);

            if (hadValidationError)
                ApplySquareStacks(payload.squareStacks);
        }

        private List<int> GetAllDescendantIds(int npcId)
        {
            var result = new List<int>();
            if (_board.NpcCubes.TryGetValue(npcId, out var cube))
                CollectDescendants(cube.transform, result);
            return result;
        }

        private static void CollectDescendants(Transform t, List<int> result)
        {
            foreach (Transform child in t)
            {
                var ctrl = child.GetComponent<NpcCubeController>();
                if (ctrl != null) result.Add(ctrl.NpcId);
                CollectDescendants(child, result);
            }
        }

        private void ShowResult(int winnerNpcId, SettlementDonePayload settlement)
        {
            var entry = _npcConfig.GetById(winnerNpcId);
            _winnerText.text = $"{entry?.npcName ?? winnerNpcId.ToString()} wins!";

            if (_board.NpcCubes.TryGetValue(winnerNpcId, out var cube))
            {
                cube.transform.DOKill();
                cube.transform.DOPunchScale(Vector3.one * 0.5f, 0.6f, 5);
            }

            if (settlement != null)
                ApplySettlementText(settlement);
            else
                _settlementText.text = "Settlement in progress ...";

            _settlementPanel.SetActive(true);
            _backButton.interactable = true;
        }

        private void ApplySettlementText(SettlementDonePayload payload)
        {
            var me = payload.playerResults?.Find(r => r.nickname == _session.Nickname);
            if (me != null)
            {
                _session.UpdateChips(_session.Chips.CurrentValue + me.winAmount);
                _settlementText.text = me.winAmount > 0
                    ? $"You won +{me.winAmount:N0} chips!"
                    : "No win this round";
            }
            else
            {
                _settlementText.text = "No bet this round";
            }
        }

        private void ReturnToLobby()
            => _sceneLoader.LoadAsync("LobbyScene").Forget();

        private static string StatusToText(string status) => status switch
        {
            "Racing"    => "Race in progress",
            "Settling"  => "",
            "Completed" => "",
            _           => status
        };

        private void OnDestroy()
        {
            _countdownCts?.Cancel();
            _countdownCts?.Dispose();
            _disposables.Dispose();
        }
    }
}
