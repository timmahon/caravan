using System.Diagnostics;
using Caravan.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Caravan.Hosting.Tests;

[CollectionDefinition(nameof(OpenTelemetryTests), DisableParallelization = true)]
public sealed class OpenTelemetryCollection;

[Collection(nameof(OpenTelemetryTests))]
public class OpenTelemetryTests
{
    [Fact]
    public async Task The_tracer_provider_receives_caravan_spans()
    {
        var exported = new List<Activity>();
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddCaravanInstrumentation()
            .AddProcessor(new CollectingProcessor(exported))
            .Build();
        using var meterProvider = Sdk.CreateMeterProviderBuilder().AddCaravanInstrumentation().Build();

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddCaravan(o => o.Name = "otel").AddRoutes(RouteBuilder.Create(b => b.From("direct:a").RouteId("a").To("mock:a")));
        using var host = builder.Build();
        await host.StartAsync();
        await host.Services.GetRequiredService<IProducerTemplate>().SendBodyAsync("direct:a", 1);
        await host.StopAsync();

        lock (exported)
        {
            Assert.Contains(exported, a => a.DisplayName == "route a" && Equals(a.GetTagItem("caravan.context.name"), "otel"));
            Assert.Contains(exported, a => a.DisplayName == "send mock:a");
        }
    }

    private sealed class CollectingProcessor(List<Activity> exported) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data)
        {
            lock (exported)
            {
                exported.Add(data);
            }
        }
    }
}
