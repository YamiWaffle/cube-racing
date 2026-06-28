using System.Collections.Generic;
using UnityEngine;

namespace CubeRacing
{
    public class RoundBottomHud : MonoBehaviour
    {
        [SerializeField] private RoundHudSlotView[] _slots;

        private NpcConfig _npcConfig;

        public void Initialize(NpcConfig npcConfig)
        {
            _npcConfig = npcConfig;
        }

        public void SetRound(Dictionary<int, int> npcSteps)
        {
            gameObject.SetActive(true);
            for (int i = 0; i < _slots.Length; i++)
            {
                var entry = _npcConfig.npcs[i];
                int? steps = npcSteps.TryGetValue(entry.id, out int s) ? s : (int?)null;
                _slots[i].Setup(entry, steps);
                _slots[i].SetActive(false);
            }
        }

        public void SetActiveNpc(int npcId)
        {
            for (int i = 0; i < _slots.Length; i++)
                _slots[i].SetActive(_npcConfig.npcs[i].id == npcId);
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
