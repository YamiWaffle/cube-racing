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

        // Assigned in Unity Editor (added to prefab in Task 4 Step 4)
        [SerializeField] private Image    _betBorder;
        [SerializeField] private TMP_Text _betBadgeText;
        [SerializeField] private Image    _greyOverlay;

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

        public void SetBetHighlight(int amount)
        {
            if (_betBorder != null)
                _betBorder.color = new Color(1f, 0.84f, 0f);   // gold
            if (_betBadgeText != null)
            {
                _betBadgeText.text    = $"✓ Bet {amount:N0}";
                _betBadgeText.gameObject.SetActive(true);
            }
        }

        public void SetGreyedOut(bool greyed)
        {
            if (_greyOverlay != null)
                _greyOverlay.gameObject.SetActive(greyed);
        }

        public void ResetBetVisuals()
        {
            if (_betBorder != null)
                _betBorder.color = Color.clear;
            if (_betBadgeText != null)
                _betBadgeText.gameObject.SetActive(false);
            SetGreyedOut(false);
        }
    }
}
