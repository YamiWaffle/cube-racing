using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CubeRacing
{
    public class RoundHudSlotView : MonoBehaviour
    {
        [SerializeField] private Image    _colorBlock;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _stepsText;
        [SerializeField] private Image    _greyOverlay;

        public void Setup(NpcEntry entry, int? steps)
        {
            _colorBlock.color = entry.color;
            _nameText.text    = entry.npcName;
            _stepsText.text   = steps.HasValue ? steps.Value.ToString() : "—";
        }

        public void SetActive(bool active)
        {
            _greyOverlay.gameObject.SetActive(!active);
        }
    }
}
