using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CubeRacing
{
    public class RaceCameraController : MonoBehaviour
    {
        [SerializeField] private Camera  _camera;
        [SerializeField] private Vector3 _followOffset    = new Vector3(0f, 8f, -4f);
        [SerializeField] private float   _focusLerpSpeed  = 8f;
        [SerializeField] private float   _followLerpSpeed = 5f;
        [SerializeField] private int     _pauseMs         = 300;
        [SerializeField] private float   _arrivalThreshold = 0.1f;

        private Transform _followTarget;
        private bool      _following;

        private void Update()
        {
            if (!_following || _followTarget == null) return;
            var desired = _followTarget.position + _followOffset;
            _camera.transform.position = Vector3.Lerp(
                _camera.transform.position, desired, _followLerpSpeed * Time.deltaTime);
        }

        public async UniTask FocusOnAsync(Transform target, CancellationToken ct)
        {
            _following = false;

            await UniTask.Delay(_pauseMs, cancellationToken: ct);

            while (!ct.IsCancellationRequested)
            {
                var desired = target.position + _followOffset;
                _camera.transform.position = Vector3.Lerp(
                    _camera.transform.position, desired, _focusLerpSpeed * Time.deltaTime);
                if (Vector3.Distance(_camera.transform.position, desired) < _arrivalThreshold)
                    break;
                await UniTask.Yield(ct);
            }

            _followTarget = target;
            _following    = true;
        }
    }
}
