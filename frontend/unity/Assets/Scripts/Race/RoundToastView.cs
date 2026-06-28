using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace CubeRacing
{
    public class RoundToastView : UIBehaviour
    {
        [SerializeField] private TMP_Text _text;
        [SerializeField] private float _displayDuration = 0.8f;
        

        public async UniTask ShowAsync(int round, CancellationToken cancellationToken)
        {
            _text.text = $"Round {round}";
            
            await UIShowAsync(cancellationToken: cancellationToken);
            
            // Hold to display
            await UniTask.Delay((int)(_displayDuration * 1000), cancellationToken: cancellationToken);
            
            await UIHideAsync(cancellationToken: cancellationToken);
        }
    }
}
