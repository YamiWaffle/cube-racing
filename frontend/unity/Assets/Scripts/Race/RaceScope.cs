using VContainer;
using VContainer.Unity;

namespace CubeRacing
{
    public class RaceScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponentInHierarchy<BoardController>();
            builder.RegisterComponentInHierarchy<RacePresenter>();
            builder.RegisterComponentInHierarchy<RaceCameraController>();
        }
    }
}
