using UnityEngine;

namespace CubeRacing
{
    public class HowToPlayPresenter : MonoBehaviour
    {
        [SerializeField] private GameObject _overlay;

        public void Show() => _overlay.SetActive(true);
        public void Hide() => _overlay.SetActive(false);
    }
}
