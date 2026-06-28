using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace CubeRacing
{
    public class DiceRollPanelView : UIBehaviour
    {
        [SerializeField] private DiceSlotView[] _slots;
        [SerializeField] private float          _rollDuration    = 1.2f;
        [SerializeField] private float          _holdDuration    = 0.5f;
        [SerializeField] private int            _shuffleSteps    = 5;
        [SerializeField] private float          _shuffleDuration = 0.15f;
        [SerializeField] private float          _settleDuration  = 0.4f;

        public async UniTask ShowAsync(
            NpcConfig npcConfig,
            int[] npcOrder,
            Dictionary<int, int> npcDice,
            CancellationToken cancellationToken)
        {
            await UIShowAsync(cancellationToken: cancellationToken);

            for (int i = 0; i < _slots.Length; i++)
                _slots[i].Setup(npcConfig.npcs[i]);

            await ShuffleOrderAsync(npcConfig, npcOrder, cancellationToken);

            var tasks = new UniTask[_slots.Length];
            for (int i = 0; i < _slots.Length; i++)
            {
                var entry = npcConfig.npcs[i];
                if (npcDice.TryGetValue(entry.id, out int dice))
                    tasks[i] = _slots[i].RollAsync(dice, _rollDuration, cancellationToken);
                else
                {
                    _slots[i].SetEmpty();
                    tasks[i] = UniTask.CompletedTask;
                }
            }

            await UniTask.WhenAll(tasks);
            await UniTask.Delay((int)(_holdDuration * 1000), cancellationToken: cancellationToken);
            await UIHideAsync(cancellationToken: cancellationToken);
        }

        private async UniTask ShuffleOrderAsync(
            NpcConfig npcConfig, int[] npcOrder, CancellationToken ct)
        {
            // 讓 LayoutGroup 算好初始位置後讀取，再暫停讓我們手動控制
            var layoutGroup = GetComponent<LayoutGroup>();
            if (layoutGroup != null) layoutGroup.enabled = true;
            LayoutRebuilder.ForceRebuildLayoutImmediate(RectTransform);

            var panelPositions = new Vector2[_slots.Length];
            for (int i = 0; i < _slots.Length; i++)
                panelPositions[i] = ((RectTransform)_slots[i].transform).anchoredPosition;

            if (layoutGroup != null) layoutGroup.enabled = false;

            // npcId → slot index（_slots[i] 對應 npcConfig.npcs[i]）
            var npcToSlot = new Dictionary<int, int>();
            for (int i = 0; i < npcConfig.npcs.Length; i++)
                npcToSlot[npcConfig.npcs[i].id] = i;

            // 追蹤目前每個位置放的 slot
            var posToSlot = Enumerable.Range(0, _slots.Length).ToArray();
            var slotToPos = Enumerable.Range(0, _slots.Length).ToArray();

            // 隨機 pair swap × _shuffleSteps
            var rng = new System.Random();
            for (int s = 0; s < _shuffleSteps; s++)
            {
                int p1    = rng.Next(_slots.Length);
                int p2    = (p1 + rng.Next(1, _slots.Length)) % _slots.Length;
                int slot1 = posToSlot[p1];
                int slot2 = posToSlot[p2];

                await UniTask.WhenAll(
                    _slots[slot1].SlideToAsync(panelPositions[p2], _shuffleDuration, ct),
                    _slots[slot2].SlideToAsync(panelPositions[p1], _shuffleDuration, ct));

                posToSlot[p1] = slot2; posToSlot[p2] = slot1;
                slotToPos[slot1] = p2; slotToPos[slot2] = p1;
            }

            // 所有 slot 同時滑到最終目標位置
            var settleTasks = new UniTask[_slots.Length];
            var assignedSlots = new HashSet<int>();
            for (int p = 0; p < npcOrder.Length && p < _slots.Length; p++)
            {
                if (!npcToSlot.TryGetValue(npcOrder[p], out int slotIdx)) continue;
                _slots[slotIdx].transform.SetSiblingIndex(p);
                settleTasks[slotIdx] = _slots[slotIdx].SlideToAsync(panelPositions[p], _settleDuration, ct);
                assignedSlots.Add(slotIdx);
            }
            // Return finished-NPC slots to their identity positions
            for (int i = 0; i < _slots.Length; i++)
            {
                if (!assignedSlots.Contains(i))
                    settleTasks[i] = _slots[i].SlideToAsync(panelPositions[i], _settleDuration, ct);
            }
            await UniTask.WhenAll(settleTasks);
        }
    }
}
