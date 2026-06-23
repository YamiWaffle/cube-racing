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

        private readonly CompositeDisposable          _disposables = new();
        private readonly Queue<RoundExecutedPayload> _roundQueue  = new();
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
            {
                _roundQueue.Enqueue(m.Payload);
                if (!_animating)
                    DrainQueueAsync(destroyCancellationToken).Forget();
            }).AddTo(_disposables);

            _raceCompletedSubscriber.Subscribe(m =>
                ShowWinnerAsync(m.WinnerNpcId, destroyCancellationToken).Forget()).AddTo(_disposables);

            _settlementSubscriber.Subscribe(m =>
                ShowSettlement(m.Payload)).AddTo(_disposables);
        }

        private async UniTaskVoid DrainQueueAsync(CancellationToken ct)
        {
            _animating = true;
            try
            {
                while (_roundQueue.Count > 0 && !ct.IsCancellationRequested)
                {
                    var payload = _roundQueue.Dequeue();
                    await PlayRoundAsync(payload, ct);
                }
            }
            finally
            {
                _animating = false;
            }
        }

        private async UniTask PlayRoundAsync(RoundExecutedPayload payload, CancellationToken ct)
        {
            if (payload.actions == null || payload.actions.Count == 0) return;

            float durationPerAction = (GameSettings.RoundIntervalMs / 1000f * 0.8f)
                                      / payload.actions.Count;

            foreach (var action in payload.actions)
            {
                var targetPos = _board.GetSquarePosition(action.toSquare);

                // Move leader NPC
                if (_board.NpcCubes.TryGetValue(action.npcId, out var cube))
                    await cube.MoveToAsync(targetPos, durationPerAction);

                // Move carried NPCs to same target (no animation — stacked with offset)
                int stackIdx = 1;
                foreach (var carriedId in action.carriedNpcIds)
                {
                    if (_board.NpcCubes.TryGetValue(carriedId, out var carried))
                    {
                        carried.transform.position = targetPos + Vector3.up * 0.5f * stackIdx;
                        stackIdx++;
                    }
                }

                await UniTask.Delay(50, cancellationToken: ct); // brief pause between actions
            }
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
            "Racing"    => "比賽進行中",
            "Settling"  => "結算中...",
            "Completed" => "比賽結束",
            _           => status
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
