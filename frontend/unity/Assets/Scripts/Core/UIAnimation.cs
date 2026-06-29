using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

namespace CubeRacing
{
    public static class UIAnimation
    {
        public static async UniTask CommonShowAsync(RectTransform uiTransform,
            float duration = 0.5f,
            CancellationToken cancellationToken = default)
        {
            uiTransform.localScale = Vector3.zero;
            uiTransform.gameObject.SetActive(true);

            if (cancellationToken.IsCancellationRequested) return;

            var tween = uiTransform.DOScale(Vector3.one, duration).SetEase(Ease.OutQuad);

            await UniTask.WhenAny(tween.ToUniTask(), UniTask.WaitUntilCanceled(cancellationToken));

            try { if (tween.IsActive()) tween.Kill(); }
            catch { }

            cancellationToken.ThrowIfCancellationRequested();
        }

        public static async UniTask CommonHideAsync(RectTransform uiTransform,
            float duration = 0.5f,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested) return;

            var tween = uiTransform.DOScale(Vector3.zero, duration).SetEase(Ease.OutQuad);

            await UniTask.WhenAny(tween.ToUniTask(), UniTask.WaitUntilCanceled(cancellationToken));

            try { if (tween.IsActive()) tween.Kill(); }
            catch { }

            if (!cancellationToken.IsCancellationRequested)
                uiTransform.gameObject.SetActive(false);

            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}