using Caravan.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Caravan.Dsl.Yaml.Tests;

public sealed class HostingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "caravan-yaml-host-" + Guid.NewGuid().ToString("N"));

    public HostingTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "routes"));
        File.WriteAllText(Path.Combine(_root, "routes", "greet.yaml"), """
            - beans:
                - name: greeter
                  type: Caravan.Dsl.Yaml.Tests.Greeter
                  properties: { greeting: "{{greeting}}" }
            - route:
                id: greet
                from:
                  uri: direct:greet
                  steps:
                    - bean: { ref: greeter, method: Greet }
            """);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task AddYamlRoutes_loads_files_relative_to_the_content_root()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true, ContentRootPath = _root });
        builder.Configuration["greeting"] = "Howdy";
        builder.Services.AddCaravan().AddYamlRoutes("routes/*.yaml");
        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            var template = host.Services.GetRequiredService<IProducerTemplate>();

            var reply = await template.RequestBodyAsync<string>("direct:greet", "Ann");

            Assert.Equal("Howdy Ann", reply);
            Assert.NotNull(host.Services.GetRequiredService<ICaravanContext>().GetRoute("greet"));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task AddYamlRoutes_with_watch_reloads_while_the_host_runs()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true, ContentRootPath = _root });
        builder.Configuration["greeting"] = "Hi";
        builder.Services.AddCaravan().AddYamlRoutes("routes", o =>
        {
            o.Watch = true;
            o.DebounceInterval = TimeSpan.FromMilliseconds(50);
        });
        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            var context = host.Services.GetRequiredService<ICaravanContext>();
            var template = host.Services.GetRequiredService<IProducerTemplate>();
            Assert.Equal("Hi Bo", await template.RequestBodyAsync<string>("direct:greet", "Bo"));

            File.WriteAllText(Path.Combine(_root, "routes", "extra.yaml"), "- route: { id: extra, from: { uri: direct:extra, steps: [ { setBody: { constant: extra } } ] } }");
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (context.GetRouteStatus("extra") != Spi.ServiceStatus.Started && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.Equal("extra", await template.RequestBodyAsync<string>("direct:extra", "x"));
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
