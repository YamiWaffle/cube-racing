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
            await transform.DOJump(target, jumpPower: 1.2f, numJumps: 1, duration: duration)
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
