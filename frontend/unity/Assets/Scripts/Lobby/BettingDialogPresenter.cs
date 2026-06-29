using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace CubeRacing
{
    public class BettingDialogPresenter : UIBehaviour
    {
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
            
            gameObject.SetActive(false);
        }

        public async UniTask Show(int npcId, CancellationToken ct)
        {
            _currentNpcId = npcId;
            var entry     = _npcConfig.GetById(npcId);
            var oddsEntry = _gameState.NpcOdds.CurrentValue?.Find(o => o.npcId == npcId);
            _currentOdds  = oddsEntry?.odds ?? 1.3;
            _amount       = 0;
            _errorText.text = string.Empty;

            _titleText.text = $"Bet on: {entry?.npcName ?? npcId.ToString()}";
            _oddsText.text  = $"Odds: {_currentOdds:F1}x";
            RefreshDisplay();

            _confirmButton.onClick.RemoveAllListeners();
            _confirmButton.onClick.AddListener(() => ConfirmAsync(ct).Forget());
            
            await UIShowAsync(cancellationToken: ct);
        }

        private void Hide()
        {
            UIHideAsync().Forget();
        }

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
            _chipsText.text     = $"Chips: {_session.Chips.CurrentValue:N0}";
            _amountText.text    = _amount.ToString("N0");
            _estimatedText.text = $"Est. return: {(int)Math.Floor(_amount * _currentOdds):N0} chips";
            _confirmButton.interactable = _amount > 0;
        }

        private async UniTaskVoid ConfirmAsync(CancellationToken ct)
        {
            _confirmButton.interactable = false;
            _errorText.text = string.Empty;
            try
            {
                await _api.PlaceBetAsync(_gameState.CurrentSessionId, _currentNpcId, _amount, ct);
                _session.UpdateChips(_session.Chips.CurrentValue - _amount);
                _gameState.HasPlacedBet.Value = true;
                _gameState.SetBet(_currentNpcId, _amount);
                Hide();
            }
            catch (ApiException ex)
            {
                _errorText.text = ex.ResponseBody.Contains("already") ? "Already bet this round"  :
                                  ex.ResponseBody.Contains("chips")   ? "Insufficient chips"      :
                                  ex.ResponseBody.Contains("closed")  ? "Betting closed"          :
                                  $"Bet failed: {ex.ResponseBody}";
                _confirmButton.interactable = _amount > 0;
            }
        }
    }
}
