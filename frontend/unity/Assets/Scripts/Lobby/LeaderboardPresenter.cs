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
            gameObject.SetActive(false);
        }

        public async UniTaskVoid Show(CancellationToken ct)
        {
            gameObject.SetActive(true);
            
            foreach (Transform child in _rowContainer) Destroy(child.gameObject);
            _notRankedText.text = string.Empty;

            List<LeaderboardEntry> entries;
            try { entries = await _api.GetLeaderboardAsync(ct); }
            catch { _notRankedText.text = "Load failed"; return; }

            bool selfFound = false;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var row = Instantiate(_rowPrefab, _rowContainer);
                var text = row.GetComponent<TMP_Text>();
                text.text = $"#{i + 1}  {e.nickname}  {e.totalChipsWon:N0}";

                if (e.nickname == _session.Nickname)
                {
                    text.color = new Color(0.839f, 0.620f, 0.180f); // highlight gold
                    selfFound  = true;
                }
            }

            if (!selfFound)
                _notRankedText.text = "(Not in top 20)";
        }

        private void Hide() => gameObject.SetActive(false);
    }
}
