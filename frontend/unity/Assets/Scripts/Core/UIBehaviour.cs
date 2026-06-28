using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CubeRacing
{
    public abstract class UIBehaviour : MonoBehaviour
    {
        private RectTransform _rectTransform;

        public RectTransform RectTransform
        {
            get
            {
                if (_rectTransform == null)
                    _rectTransform = GetComponent<RectTransform>();
                
                return _rectTransform;
            }
        }

        protected virtual UniTask UIShowAsync(float duration = 0.3f, CancellationToken cancellationToken = default)
        {
            return UIAnimation.CommonShowAsync(RectTransform, duration, cancellationToken);
        }
        
        protected virtual UniTask UIHideAsync(float duration = 0.3f, CancellationToken cancellationToken = default)
        {
            return UIAnimation.CommonHideAsync(RectTransform, duration, cancellationToken);
        }
    }
}