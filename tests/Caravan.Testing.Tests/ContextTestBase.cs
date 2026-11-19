using Caravan.Builder;
using Caravan.Components.Mock;

namespace Caravan.Testing.Tests;

/// <summary>Base class: a fresh context and template per test.</summary>
public abstract class ContextTestBase : IAsyncLifetime
{
    protected static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

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

    protected void AddRoutes(Action<RouteBuilder> configure) => Context.AddRoutes(RouteBuilder.Create(configure));

    protected async Task StartWithRoutes(Action<RouteBuilder> configure)
    {
        AddRoutes(configure);
        await Context.StartAsync();
    }

    protected MockEndpoint Mock(string name) => Context.GetMockEndpoint(name);
}
