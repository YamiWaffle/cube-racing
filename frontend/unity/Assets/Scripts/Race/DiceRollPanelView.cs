using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CubeRacing
{
    public class DiceRollPanelView : MonoBehaviour
    {
        [SerializeField] private DiceSlotView[] _slots;
        [SerializeField] private float          _rollDuration = 1.2f;
        [SerializeField] private float          _holdDuration = 0.5f;

        public async UniTask ShowAsync(
            NpcConfig npcConfig,
            Dictionary<int, int> npcDice,
            CancellationToken ct)
        {
            gameObject.SetActive(true);

            var tasks = new UniTask[_slots.Length];
            for (int i = 0; i < _slots.Length; i++)
            {
                var entry = npcConfig.npcs[i];
                _slots[i].Setup(entry);
                if (npcDice.TryGetValue(entry.id, out int dice))
                    tasks[i] = _slots[i].RollAsync(dice, _rollDuration, ct);
                else
                {
                    _slots[i].SetEmpty();
                    tasks[i] = UniTask.CompletedTask;
                }
            }

            await UniTask.WhenAll(tasks);
            await UniTask.Delay((int)(_holdDuration * 1000), cancellationToken: ct);
            gameObject.SetActive(false);
        }
    }
}
