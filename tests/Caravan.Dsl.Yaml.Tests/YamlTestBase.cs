using Caravan.Components.Mock;
using Caravan.Dsl.Yaml;

namespace Caravan.Dsl.Yaml.Tests;

public abstract class YamlTestBase : IAsyncLifetime
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

    /// <summary>Adds the YAML routes and starts the context.</summary>
    protected async Task StartAsync(string yaml)
    {
        Context.AddRoutes(YamlRoutesBuilder.FromString(yaml, "test.yaml"));
        await Context.StartAsync();
    }

    protected MockEndpoint Mock(string name) => Context.GetMockEndpoint(name);

    /// <summary>The error raised when adding <paramref name="yaml"/>.</summary>
    protected YamlRouteDefinitionException LoadError(string yaml) =>
        Assert.Throws<YamlRouteDefinitionException>(() => Context.AddRoutes(YamlRoutesBuilder.FromString(yaml, "test.yaml")));
}
