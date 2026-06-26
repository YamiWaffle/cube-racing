using Cysharp.Threading.Tasks;
using VContainer;
using VContainer.Unity;

namespace CubeRacing
{
    public class AppBootstrapper : IStartable
    {
        private GameStateService  _gameStateService;
        private SceneLoader _sceneLoader;

        [Inject]
        public void Construct(GameStateService gameStateService, SceneLoader sceneLoader)
        {
            _gameStateService = gameStateService;
            _sceneLoader = sceneLoader;
        }

        public void Start()
        {
            _gameStateService.Initialize();
            _sceneLoader.LoadAsync("LoginScene").Forget();
        }
    }
}
