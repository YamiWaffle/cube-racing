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

        // Cached once after the first UIShowAsync, when the panel is visible and the
        // LayoutGroup has computed real positions in sibling order 0,1,2,…
        // Reused every round so we are never reading scrambled post-settle positions.
        private Vector2[] _canonicalPositions;
        private readonly Dictionary<int, int> _npcIdToSlotMap = new();

        public async UniTask ShowAsync(
            NpcConfig npcConfig,
            int[] npcOrder,
            Dictionary<int, int> npcDice,
            CancellationToken cancellationToken)
        {
            // Round 2+: snap slots back to canonical order while the panel is still
            // hidden so the user never sees the scrambled state from the previous round.
            if (_canonicalPositions != null)
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    _slots[i].transform.SetSiblingIndex(i);
                    ((RectTransform)_slots[i].transform).anchoredPosition = _canonicalPositions[i];
                }
            }

            await UIShowAsync(cancellationToken: cancellationToken);

            // Round 1: panel is now visible and the LayoutGroup has done its first
            // layout pass — read and cache canonical positions for all future rounds.
            if (_canonicalPositions == null)
            {
                var lg = GetComponent<LayoutGroup>();
                if (lg != null) lg.enabled = true;
                LayoutRebuilder.ForceRebuildLayoutImmediate(RectTransform);
                _canonicalPositions = new Vector2[_slots.Length];
                for (int i = 0; i < _slots.Length; i++)
                    _canonicalPositions[i] = ((RectTransform)_slots[i].transform).anchoredPosition;
                if (lg != null) lg.enabled = false;
            }

            _npcIdToSlotMap.Clear();
            for (int i = 0; i < _slots.Length && i < npcConfig.npcs.Length; i++)
            {
                _slots[i].Setup(npcConfig.npcs[i]);
                _npcIdToSlotMap[npcConfig.npcs[i].id] = i;
            }

            await ShuffleOrderAsync(npcOrder, cancellationToken);

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

        private async UniTask ShuffleOrderAsync(int[] npcOrder, CancellationToken ct)
        {
            // Slots are guaranteed to be at _canonicalPositions[i] when this runs.
            var posToSlot = Enumerable.Range(0, _slots.Length).ToArray();
            var slotToPos = Enumerable.Range(0, _slots.Length).ToArray();

            var rng = new System.Random();
            for (int s = 0; s < _shuffleSteps; s++)
            {
                int p1    = rng.Next(_slots.Length);
                int p2    = (p1 + rng.Next(1, _slots.Length)) % _slots.Length;
                int slot1 = posToSlot[p1];
                int slot2 = posToSlot[p2];

                await UniTask.WhenAll(
                    _slots[slot1].SlideToAsync(_canonicalPositions[p2], _shuffleDuration, ct),
                    _slots[slot2].SlideToAsync(_canonicalPositions[p1], _shuffleDuration, ct));

                posToSlot[p1] = slot2; posToSlot[p2] = slot1;
                slotToPos[slot1] = p2; slotToPos[slot2] = p1;
            }

            var settleTasks = new UniTask[_slots.Length];
            var assignedSlots = new HashSet<int>();
            for (int i = 0; i < npcOrder.Length && i < _slots.Length; i++)
            {
                if (!_npcIdToSlotMap.TryGetValue(npcOrder[i], out int slotIdx)) continue;
                _slots[slotIdx].transform.SetSiblingIndex(i);
                settleTasks[slotIdx] = _slots[slotIdx].SlideToAsync(_canonicalPositions[i], _settleDuration, ct);
                assignedSlots.Add(slotIdx);
            }
            for (int i = 0; i < _slots.Length; i++)
            {
                if (!assignedSlots.Contains(i))
                    settleTasks[i] = _slots[i].SlideToAsync(_canonicalPositions[i], _settleDuration, ct);
            }
            await UniTask.WhenAll(settleTasks);
        }
    }
}
