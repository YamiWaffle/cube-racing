using DG.Tweening;
using MessagePipe;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace CubeRacing
{
    public class Main : LifetimeScope
    {
        private const string ApiBaseUrl  = "http://localhost:5062";
        private const string SignalRUrl  = "ws://localhost:5062/hubs/game";

        [SerializeField] private NpcConfig _npcConfig;

        protected override void Configure(IContainerBuilder builder)
        {
            DOTween.Init();

            if (_npcConfig == null)
                throw new System.InvalidOperationException("[Main] NpcConfig is not assigned in the Inspector.");

            // MessagePipe
            builder.RegisterMessagePipe();
            
            // Setup GlobalMessagePipe to enable diagnostics window and global function
            builder.RegisterBuildCallback(c =>
                GlobalMessagePipe.SetProvider(c.AsServiceProvider()));

            // Network
            builder.Register<ApiClient>(Lifetime.Singleton)
                   .WithParameter("baseUrl", ApiBaseUrl);
            builder.Register<SignalRClient>(Lifetime.Singleton)
                   .WithParameter("url", SignalRUrl);

            // Core
            builder.Register<PlayerSession>(Lifetime.Singleton);
            builder.Register<GameStateService>(Lifetime.Singleton);
            builder.RegisterEntryPoint<AppBootstrapper>();

            // Config
            builder.RegisterInstance(_npcConfig);

            // Scene management
            builder.Register<SceneLoader>(Lifetime.Singleton);
        }
    }
}
