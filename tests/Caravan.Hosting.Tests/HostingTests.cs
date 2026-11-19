using Caravan.Builder;
using Caravan.Components.Mock;
using Caravan.Hosting;
using Caravan.Spi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Caravan.Hosting.Tests;

public sealed class Greeter
{
    public string Greet(string name) => "Hello " + name;
}

/// <summary>A route builder with constructor-injected services.</summary>
public sealed class GreetingRoutes(Greeter greeter) : RouteBuilder
{
    public override void Configure()
    {
        From("direct:greet").RouteId("greet").Process(e => e.Message.Body = greeter.Greet(e.Message.GetBody<string>()!));
        From("direct:bean").RouteId("bean").Bean(typeof(Greeter), "Greet");
        From("direct:queue").RouteId("queue").To("{{app.target}}");
    }
}

public sealed class FailingConfigurations : RouteConfigurationBuilder
{
    public override void Configuration() => RouteConfiguration(c => c.OnException<InvalidOperationException>(o => o.Handled(true).SetBody(Expr.Constant("recovered"))));
}

public sealed class FailingRoutes : RouteBuilder
{
    public override void Configure() => From("direct:fail").RouteId("fail").Process(_ => throw new InvalidOperationException("boom"));
}

public class HostingTests
{
    private static HostApplicationBuilder NewBuilder(Dictionary<string, string?>? config = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(config ?? []);
        return builder;
    }

    [Fact]
    public async Task The_host_starts_and_stops_the_context_with_DI_injected_route_builders_and_beans()
    {
        var builder = NewBuilder(new() { ["app:target"] = "mock:queued" });
        builder.Services.AddSingleton<Greeter>();
        builder.Services.AddCaravan().AddRoutes<GreetingRoutes>();
        using var host = builder.Build();
        var context = host.Services.GetRequiredService<ICaravanContext>();
        Assert.Same(context, host.Services.GetRequiredService<CaravanContext>());
        Assert.Equal(ServiceStatus.Initialized, context.Status);

        await host.StartAsync();
        Assert.Equal(ServiceStatus.Started, context.Status);

        var template = host.Services.GetRequiredService<IProducerTemplate>();
        Assert.Same(template, host.Services.GetRequiredService<IProducerTemplate>());
        Assert.Equal("Hello DI", await template.RequestBodyAsync<string>("direct:greet", "DI"));
        Assert.Equal("Hello bean", await template.RequestBodyAsync<string>("direct:bean", "bean"));

        var mock = context.GetEndpoint<MockEndpoint>("mock:queued").ExpectedBodiesReceived("from config");
        await template.SendBodyAsync("direct:queue", "from config");
        await mock.AssertIsSatisfiedAsync();

        await host.StopAsync();
        Assert.Equal(ServiceStatus.Stopped, context.Status);
    }

    [Fact]
    public async Task Options_are_bound_from_the_Caravan_section_then_configured_in_code()
    {
        var builder = NewBuilder(new()
        {
            ["Caravan:Name"] = "from-config",
            ["app:target"] = "mock:t",
            ["Caravan:AutoStartup"] = "false",
            ["Caravan:ShutdownTimeout"] = "00:00:07",
            ["Caravan:MessageHistory"] = "true",
            ["Caravan:TracingEnabled"] = "true",
            ["Caravan:StreamCaching"] = "false",
            ["Caravan:RouteFilterExcludePattern"] = "bean",
            ["Caravan:StatisticsLevel"] = "RoutesOnly",
        });
        builder.Services.AddSingleton<Greeter>();
        builder.Services.AddCaravan(o => o.Name += "-and-code").AddRoutes<GreetingRoutes>();
        using var host = builder.Build();
        await host.StartAsync();

        var context = host.Services.GetRequiredService<ICaravanContext>();
        Assert.Equal("from-config-and-code", context.Name);
        Assert.False(context.AutoStartup);
        Assert.Equal(TimeSpan.FromSeconds(7), context.ShutdownStrategy.Timeout);
        Assert.True(context.MessageHistory);
        Assert.True(context.Tracing);
        Assert.False(context.StreamCaching.Enabled);
        Assert.Equal(Management.ManagementStatisticsLevel.RoutesOnly, context.Management.StatisticsLevel);
        Assert.Null(context.GetRoute("bean"));
        Assert.Equal(ServiceStatus.Initialized, context.GetRouteStatus("greet"));
        await host.StopAsync();
    }

    [Fact]
    public async Task Components_configurers_route_configurations_and_time_provider_come_from_DI()
    {
        var time = new FakeTimeProvider();
        var builder = NewBuilder();
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddCaravan()
            .AddComponent("audit", _ => new MockComponent())
            .Configure((sp, ctx) => ctx.Name = "configured")
            .AddRouteConfiguration<FailingConfigurations>()
            .AddRoutes<FailingRoutes>()
            .AddRoutes(RouteBuilder.Create(b => b.From("direct:audit").To("audit:x")));
        builder.Services.AddCaravan(); // idempotent
        using var host = builder.Build();
        await host.StartAsync();

        var context = host.Services.GetRequiredService<ICaravanContext>();
        Assert.Equal("configured", context.Name);
        Assert.Same(time, context.TimeProvider);
        Assert.IsType<MockComponent>(context.GetComponent("audit"));
        var template = host.Services.GetRequiredService<IProducerTemplate>();
        Assert.Equal("recovered", await template.RequestBodyAsync<string>("direct:fail", "x"));
        Assert.Single(host.Services.GetServices<IHostedService>().OfType<CaravanHostedService>());
        Assert.NotNull(host.Services.GetRequiredService<IConsumerTemplate>());
        Assert.Same(context.Management, host.Services.GetRequiredService<Management.IManagementService>());
        await host.StopAsync();
    }

    [Fact]
    public async Task Stopping_the_host_waits_for_inflight_exchanges()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = false;
        var builder = NewBuilder();
        builder.Services.AddCaravan().AddRoutes(RouteBuilder.Create(b => b.From("seda:work").Process(async _ =>
        {
            entered.SetResult();
            await release.Task;
            completed = true;
        })));
        using var host = builder.Build();
        await host.StartAsync();
        await host.Services.GetRequiredService<IProducerTemplate>().SendBodyAsync("seda:work", 1);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var stop = host.StopAsync();
        await Task.Delay(50);
        Assert.False(stop.IsCompleted);
        release.SetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(completed);
    }

    [Fact]
    public async Task The_host_shutdown_token_forces_the_shutdown()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = NewBuilder();
        builder.Services.AddCaravan(o => o.ShutdownTimeout = TimeSpan.FromMinutes(5)).AddRoutes(RouteBuilder.Create(b => b.From("seda:work").Process(async e =>
        {
            entered.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, e.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancelled.SetResult();
                throw;
            }
        })));
        using var host = builder.Build();
        await host.StartAsync();
        var context = host.Services.GetRequiredService<ICaravanContext>();
        await host.Services.GetRequiredService<IProducerTemplate>().SendBodyAsync("seda:work", 1);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        using var hostTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await host.StopAsync(hostTimeout.Token).WaitAsync(TimeSpan.FromSeconds(10));

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ServiceStatus.Stopped, context.Status);
        Assert.True(context.ShutdownStrategy.HasTimeoutOccurred);
    }
}
