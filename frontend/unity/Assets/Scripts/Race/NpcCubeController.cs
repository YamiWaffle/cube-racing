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
            await transform.DOJump(target, jumpPower: 1.2f, numJumps: 1, duration: duration)
                           .SetEase(Ease.InOutSine)
                           .ToUniTask(cancellationToken: ct);
        }
    }
}
