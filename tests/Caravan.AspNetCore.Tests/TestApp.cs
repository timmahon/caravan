using Caravan.Builder;
using Caravan.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Caravan.AspNetCore.Tests;

/// <summary>A minimal-API application on a TestServer (no real port) with Caravan, platform-http and the given routes.</summary>
internal sealed class TestApp : IAsyncDisposable
{
    private TestApp(WebApplication app)
    {
        App = app;
        Client = app.GetTestClient();
    }

    public WebApplication App { get; }

    public HttpClient Client { get; }

    public ICaravanContext Context => App.Services.GetRequiredService<ICaravanContext>();

    public static Task<TestApp> StartAsync(Action<RouteBuilder> routes, Action<CaravanEndpointOptions>? endpoints = null) =>
        StartAsync(c => c.AddRoutes(RouteBuilder.Create(routes)), endpoints);

    public static async Task<TestApp> StartAsync(
        Action<ICaravanBuilder> caravan,
        Action<CaravanEndpointOptions>? endpoints = null,
        Action<PlatformHttpComponent>? component = null,
        Action<WebApplication>? map = null)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var caravanBuilder = builder.Services.AddCaravan(o => o.ShutdownTimeout = TimeSpan.FromSeconds(5)).AddAspNetCore(component);
        caravan(caravanBuilder);
        var app = builder.Build();
        app.MapCaravan(endpoints);
        map?.Invoke(app);
        await app.StartAsync();
        return new TestApp(app);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
    }
}
