using Caravan.Builder;
using Caravan.Components.Mock;

namespace Caravan.Core.Tests;

/// <summary>Base class: a fresh context per test, started after routes are added.</summary>
public abstract class ContextTestBase : IAsyncLifetime
{
    protected CaravanContext Context { get; } = new();

    protected IProducerTemplate Template { get; private set; } = null!;

    public virtual Task InitializeAsync()
    {
        Template = Context.CreateProducerTemplate();
        return Task.CompletedTask;
    }

    public virtual async Task DisposeAsync()
    {
        await Template.DisposeAsync();
        await Context.DisposeAsync();
    }

    protected async Task StartWithRoutes(Action<RouteBuilder> configure)
    {
        Context.AddRoutes(RouteBuilder.Create(configure));
        await Context.StartAsync();
    }

    protected MockEndpoint Mock(string name) => Context.GetMockEndpoint(name);
}
