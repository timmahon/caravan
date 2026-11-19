using System.Net;
using System.Text.Json.Nodes;
using Caravan.Builder;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Caravan.AspNetCore.Tests;

public class ManagementAndHealthTests
{
    private static void Routes(RouteBuilder b)
    {
        b.From("platform-http:/ping").RouteId("ping").SetBody(Expr.Constant("pong"));
        b.From("direct:idle").RouteId("idle").To("mock:idle");
    }

    [Fact]
    public async Task Health_endpoints_report_the_context_and_routes()
    {
        await using var app = await TestApp.StartAsync(Routes);

        using var health = await app.Client.GetAsync("/caravan/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        var body = JsonNode.Parse(await health.Content.ReadAsStringAsync())!;
        Assert.Equal("UP", (string?)body["status"]);
        Assert.Contains(body["checks"]!.AsArray(), c => (string?)c!["id"] == "route:ping" && (string?)c["status"] == "UP");

        var live = JsonNode.Parse(await app.Client.GetStringAsync("/caravan/health/live"))!;
        Assert.Equal(["context"], live["checks"]!.AsArray().Select(c => (string?)c!["id"]));
        var ready = JsonNode.Parse(await app.Client.GetStringAsync("/caravan/health/ready"))!;
        Assert.Contains(ready["checks"]!.AsArray(), c => (string?)c!["id"] == "route:idle");

        await app.Context.StopRouteAsync("idle");
        var degraded = JsonNode.Parse(await app.Client.GetStringAsync("/caravan/health"))!;
        Assert.Equal("DEGRADED", (string?)degraded["status"]);
    }

    [Fact]
    public async Task Health_answers_503_when_down()
    {
        await using var app = await TestApp.StartAsync(Routes);
        await app.Context.StopAsync();

        using var response = await app.Client.GetAsync("/caravan/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("DOWN", (string?)JsonNode.Parse(await response.Content.ReadAsStringAsync())!["status"]);
    }

    [Fact]
    public async Task Management_is_not_mapped_by_default()
    {
        await using var app = await TestApp.StartAsync(Routes);

        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync("/caravan/management/routes")).StatusCode);
    }

    [Fact]
    public async Task Management_reports_statistics_and_controls_routes()
    {
        await using var app = await TestApp.StartAsync(Routes, o => o.Management = true);
        Assert.Equal("pong", await app.Client.GetStringAsync("/ping"));

        var context = JsonNode.Parse(await app.Client.GetStringAsync("/caravan/management"))!;
        Assert.Equal("Started", (string?)context["status"]);
        Assert.Equal(2, (int)context["routes"]!);
        Assert.Equal(1, (long)context["statistics"]!["exchangesTotal"]!);

        var routes = JsonNode.Parse(await app.Client.GetStringAsync("/caravan/management/routes"))!.AsArray();
        Assert.Equal(2, routes.Count);
        var ping = JsonNode.Parse(await app.Client.GetStringAsync("/caravan/management/routes/ping"))!;
        Assert.Equal("ping", (string?)ping["routeId"]);
        Assert.Equal("Started", (string?)ping["status"]);
        Assert.Equal(1, (long)ping["statistics"]!["exchangesTotal"]!);
        Assert.NotEmpty(JsonNode.Parse(await app.Client.GetStringAsync("/caravan/management/routes/ping/processors"))!.AsArray());
        Assert.Contains("platform-http:/ping", await app.Client.GetStringAsync("/caravan/management/routes/ping/dump"), StringComparison.Ordinal);
        Assert.Contains("platform-http", await app.Client.GetStringAsync("/caravan/management/components"), StringComparison.Ordinal);
        Assert.Contains("platform-http:/ping", await app.Client.GetStringAsync("/caravan/management/endpoints"), StringComparison.Ordinal);
        Assert.Equal("[]", await app.Client.GetStringAsync("/caravan/management/inflight"));

        using var stop = await app.Client.PostAsync("/caravan/management/routes/ping/stop", null);
        Assert.Equal(HttpStatusCode.OK, stop.StatusCode);
        Assert.Equal("Stopped", (string?)JsonNode.Parse(await stop.Content.ReadAsStringAsync())!["status"]);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync("/ping")).StatusCode);

        using var start = await app.Client.PostAsync("/caravan/management/routes/ping/start", null);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal("pong", await app.Client.GetStringAsync("/ping"));

        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.PostAsync("/caravan/management/routes/nope/stop", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.PostAsync("/caravan/management/routes/ping/explode", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync("/caravan/management/routes/nope")).StatusCode);
    }

    [Fact]
    public async Task Conventions_apply_to_every_caravan_endpoint()
    {
        var marker = new object();
        await using var app = await TestApp.StartAsync(
            c => c.AddRoutes(RouteBuilder.Create(Routes)),
            map: a =>
            {
                a.MapCaravan(o => o.Health = false).Add(b => b.Metadata.Add(marker));
                a.MapGet("/open", () => "open");
            },
            endpoints: o => o.Management = true);

        var endpoints = app.App.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints;
        Assert.All(app.App.Services.GetRequiredService<PlatformHttpEngine>().EndpointDataSource.Endpoints, e => Assert.Contains(marker, e.Metadata));
        Assert.DoesNotContain(marker, endpoints.Single(e => e.DisplayName?.Contains("/open", StringComparison.Ordinal) == true).Metadata);

        await app.Context.AddRoutesAsync(RouteBuilder.Create(b => b.From("platform-http:/later").SetBody(Expr.Constant("later"))));
        Assert.Contains(marker, app.App.Services.GetRequiredService<PlatformHttpEngine>().EndpointDataSource.Endpoints
            .Single(e => e.DisplayName!.Contains("/later", StringComparison.Ordinal)).Metadata);
        Assert.Equal("later", await app.Client.GetStringAsync("/later"));
    }
}
