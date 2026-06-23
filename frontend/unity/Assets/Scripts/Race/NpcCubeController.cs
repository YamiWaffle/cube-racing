using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

namespace CubeRacing
{
    public class NpcCubeController : MonoBehaviour
    {
        private int _npcId;
        private int _stackIndex; // vertical offset for stacking

        public void Initialize(int npcId, Color color)
        {
            _npcId = npcId;
            var renderer = GetComponent<Renderer>();
            if (renderer != null) renderer.material.color = color;
        }

        public async UniTask MoveToAsync(Vector3 target, float duration)
        {
            // Hop arc: move through a midpoint above, then land at target
            var mid = (transform.position + target) * 0.5f + Vector3.up * 1.2f;
            await transform.DOPath(new[] { mid, target }, duration, PathType.CatmullRom)
                           .SetEase(Ease.InOutSine)
                           .ToUniTask();
        }

        public void SetStackOffset(int index)
        {
            _stackIndex = index;
            var pos = transform.position;
            transform.position = new Vector3(pos.x, 0.5f + index * 0.5f, pos.z);
        }
    }
}
