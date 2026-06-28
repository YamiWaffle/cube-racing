using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CubeRacing
{
    public class DiceSlotView : MonoBehaviour
    {
        [SerializeField] private Image    _colorBlock;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _numberText;
        [SerializeField] private float    _rollIntervalSec = 0.08f;

        public void Setup(NpcEntry entry)
        {
            _colorBlock.color = entry.color;
            _nameText.text    = entry.npcName;
            _numberText.text  = string.Empty;
        }

        public void SetEmpty()
        {
            _numberText.text = string.Empty;
        }

        public async UniTask RollAsync(int finalValue, float rollDuration, CancellationToken ct)
        {
            float elapsed = 0f;
            while (elapsed < rollDuration && !ct.IsCancellationRequested)
            {
                _numberText.text = Random.Range(1, 4).ToString();
                await UniTask.Delay((int)(_rollIntervalSec * 1000), cancellationToken: ct);
                elapsed += _rollIntervalSec;
            }
            _numberText.text = finalValue.ToString();
        }

        public UniTask SlideToAsync(Vector2 targetAnchoredPos, float duration, CancellationToken ct)
        {
            return ((RectTransform)transform)
                .DOAnchorPos(targetAnchoredPos, duration)
                .SetEase(Ease.InOutSine)
                .ToUniTask(cancellationToken: ct);
        }
    }
}
