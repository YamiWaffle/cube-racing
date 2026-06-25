using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CubeRacing
{
    public class NpcCardView : MonoBehaviour
    {
        [SerializeField] private Image    _colorBlock;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _oddsText;
        [SerializeField] private Button   _betButton;
        [SerializeField] private TMP_Text _betButtonText;

        public event Action<int> OnBetClicked;
        private int _npcId;

        private void Awake()
        {
            _betButton.onClick.AddListener(() => OnBetClicked?.Invoke(_npcId));
        }

        public void SetNpc(NpcEntry entry, double odds)
        {
            _npcId              = entry.id;
            _colorBlock.color   = entry.color;
            _nameText.text      = entry.npcName;
            _oddsText.text      = $"{odds:F1}x";
            _betButtonText.text = "Bet";
        }

        public void UpdateOdds(double odds) => _oddsText.text = $"{odds:F1}x";

        public void SetBettingEnabled(bool enabled) => _betButton.interactable = enabled;

        public void SetBetPlaced(bool placed)
        {
            _betButton.interactable = false;
            if (placed) _betButtonText.text = "Bet Placed";
        }
    }
}
