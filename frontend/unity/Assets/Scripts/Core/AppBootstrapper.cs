using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;

namespace CubeRacing
{
    public class AppBootstrapper : MonoBehaviour
    {
        private SceneLoader _sceneLoader;

        [Inject]
        public void Construct(SceneLoader sceneLoader) => _sceneLoader = sceneLoader;

        private void Start()
            => _sceneLoader.LoadAsync("LoginScene").Forget();
    }
}
