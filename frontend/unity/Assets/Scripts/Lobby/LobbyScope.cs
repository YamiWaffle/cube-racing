using VContainer;
using VContainer.Unity;

namespace CubeRacing
{
    public class LobbyScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponentInHierarchy<LobbyPresenter>();
            builder.RegisterComponentInHierarchy<BettingDialogPresenter>();
            builder.RegisterComponentInHierarchy<LeaderboardPresenter>();
        }
    }
}
