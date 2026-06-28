using System.Collections.Generic;
using UnityEngine;

namespace CubeRacing
{
    public class RoundBottomHud : MonoBehaviour
    {
        [SerializeField] private RoundHudSlotView[] _slots;

        private NpcConfig _npcConfig;
        private int[]     _npcOrder;

        public void Initialize(NpcConfig npcConfig)
        {
            _npcConfig = npcConfig;
        }

        public void SetRound(int[] npcOrder, Dictionary<int, int> npcSteps)
        {
            _npcOrder = npcOrder;
            gameObject.SetActive(true);
            for (int i = 0; i < _slots.Length; i++)
            {
                if (i < npcOrder.Length)
                {
                    _slots[i].gameObject.SetActive(true);
                    var entry = _npcConfig.GetById(npcOrder[i]);
                    if (entry == null) continue;
                    int? steps = npcSteps.TryGetValue(npcOrder[i], out int s) ? s : (int?)null;
                    _slots[i].Setup(entry, steps);
                    _slots[i].SetActive(false);
                }
                else
                {
                    _slots[i].gameObject.SetActive(false);
                }
            }
        }

        public void SetActiveNpc(int npcId)
        {
            if (_npcOrder == null) return;
            for (int i = 0; i < _slots.Length && i < _npcOrder.Length; i++)
                _slots[i].SetActive(_npcOrder[i] == npcId);
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
