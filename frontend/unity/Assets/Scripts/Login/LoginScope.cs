using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace CubeRacing
{
    public class LoginScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponentInHierarchy<LoginPresenter>();
        }
    }
}
