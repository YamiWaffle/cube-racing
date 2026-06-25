using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace CubeRacing
{
    public class SceneLoader
    {
        private readonly LifetimeScope _mainScope;
        private UnityEngine.SceneManagement.Scene _currentSubScene;

        public SceneLoader(LifetimeScope mainScope) => _mainScope = mainScope;

        public async UniTask LoadAsync(string sceneName)
        {
            if (_currentSubScene.IsValid())
                await SceneManager.UnloadSceneAsync(_currentSubScene).ToUniTask();

            using (LifetimeScope.EnqueueParent(_mainScope))
                await SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive).ToUniTask();

            _currentSubScene = SceneManager.GetSceneByName(sceneName);
        }
    }
}
