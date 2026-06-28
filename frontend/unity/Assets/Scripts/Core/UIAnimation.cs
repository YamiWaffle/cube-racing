using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

namespace CubeRacing
{
    public static class UIAnimation
    {
        public static UniTask CommonShowAsync(RectTransform uiTransform,
            float duration = 0.5f,
            CancellationToken cancellationToken = default)
        {
            uiTransform.localScale = Vector3.zero;
            
            uiTransform.gameObject.SetActive(true);
            
            return uiTransform.DOScale(Vector3.one, duration)
                .SetEase(Ease.OutQuad)
                .ToUniTask(cancellationToken: cancellationToken);
        }

        public static async UniTask CommonHideAsync(RectTransform uiTransform,
            float duration = 0.5f,
            CancellationToken cancellationToken = default)
        {
            await uiTransform.DOScale(Vector3.zero, duration)
                .SetEase(Ease.OutQuad)
                .ToUniTask(cancellationToken: cancellationToken);
            
            uiTransform.gameObject.SetActive(false);
        }
    }
}