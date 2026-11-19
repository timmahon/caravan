using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Caravan.Samples.WebApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;

namespace Caravan.Samples.Tests;

/// <summary>The web API sample on a test server: C# and YAML REST routes, health, OpenAPI.</summary>
public sealed class WebApiSampleTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _app = WebApiSample.Create(["--Sample:WatchRoutes=false"], b =>
        {
            b.WebHost.UseTestServer();
            b.Logging.ClearProviders();
        });
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Stock_is_served_by_the_csharp_rest_route()
    {
        var stock = await _client.GetFromJsonAsync<JsonObject>("/api/stock/widget");
        var missing = await _client.GetAsync("/api/stock/nope");

        Assert.Equal(100, (int)stock!["quantity"]!);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Orders_are_placed_by_the_yaml_rest_route()
    {
        var accepted = await _client.PostAsJsonAsync("/api/orders", new { sku = "gadget", quantity = 2 });
        var rejected = await _client.PostAsJsonAsync("/api/orders", new { sku = "gadget", quantity = 50 });

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var confirmation = await accepted.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("gadget", (string)confirmation!["sku"]!);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Contains("Only 3 gadget in stock", await rejected.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_and_open_api_are_exposed()
    {
        var health = await _client.GetAsync("/health");
        var caravanHealth = await _client.GetAsync("/caravan/health");
        var openApi = await _client.GetFromJsonAsync<JsonObject>("/openapi.json");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("Healthy", await health.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, caravanHealth.StatusCode);
        var paths = openApi!["paths"]!.AsObject();
        Assert.Contains("/api/stock/{sku}", paths.Select(p => p.Key));
        Assert.Contains("/api/orders", paths.Select(p => p.Key));
    }
}
