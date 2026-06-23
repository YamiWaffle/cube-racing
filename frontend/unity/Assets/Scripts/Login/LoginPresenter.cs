using System;
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

        private PlayerSession    _session;
        private ApiClient        _api;
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
                _nicknameInput.text   = _session.SavedNickname;
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
            SetLoading(true);
            try
            {
                _session.LoadFromPrefs();
                _api.SetTokenProvider(() => _session.Token.ToString());
                await LoadLobbyAsync(ct);
            }
            catch (Exception e)
            {
                _errorText.text = $"載入失敗，請重新登入：{e.Message}";
                // Clear saved session so user can log in fresh
                UnityEngine.PlayerPrefs.DeleteKey("player_token");
                _loginButtonText.text = "登入";
                _loginButton.onClick.RemoveAllListeners();
                _loginButton.onClick.AddListener(() => LoginAsync(destroyCancellationToken).Forget());
            }
            finally
            {
                SetLoading(false);
            }
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
