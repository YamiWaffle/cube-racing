using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

namespace CubeRacing
{
    public class NpcCubeController : MonoBehaviour
    {
        public int NpcId { get; private set; }

        public void Initialize(int npcId, Color color)
        {
            NpcId = npcId;
            var renderer = GetComponent<Renderer>();
            if (renderer != null) renderer.material.color = color;
        }

        public async UniTask MoveToAsync(Vector3 target, float duration, CancellationToken ct = default)
        {
            transform.DOKill();
            if (ct.IsCancellationRequested) return;

            var tween = transform.DOJump(target, jumpPower: 1.2f, numJumps: 1, duration: duration)
                                 .SetEase(Ease.InOutSine);

            // Don't pass ct to ToUniTask — avoids registering a kill callback on ct,
            // which can double-kill the tween when the scene tears down (DOTween bug).
            await UniTask.WhenAny(tween.ToUniTask(), UniTask.WaitUntilCanceled(ct));

            try { if (tween.IsActive()) tween.Kill(); }
            catch { }

            ct.ThrowIfCancellationRequested();
        }
    }
}
