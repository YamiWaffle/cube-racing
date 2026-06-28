using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace CubeRacing
{
    public class RoundToastView : MonoBehaviour
    {
        [SerializeField] private TMP_Text _text;
        [SerializeField] private float    _displayDuration = 0.8f;

        public async UniTask ShowAsync(int round, CancellationToken ct)
        {
            _text.text = $"Round {round}";
            gameObject.SetActive(true);
            await UniTask.Delay((int)(_displayDuration * 1000), cancellationToken: ct);
            gameObject.SetActive(false);
        }
    }
}
