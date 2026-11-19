using System.Net;
using System.Text.Json.Nodes;
using Caravan.AspNetCore.OpenApi;
using Caravan.Builder;
using Caravan.Model.Rest;

namespace Caravan.AspNetCore.Tests;

public class OpenApiTests
{
    private static void Api(RouteBuilder b)
    {
        b.RestConfiguration(c => c
            .BindingMode(RestBindingMode.Json)
            .ContextPath("/rest")
            .ApiContextPath("/api-doc")
            .ApiProperty("api.title", "Orders API")
            .ApiProperty("api.version", "2.1")
            .ApiProperty("api.description", "Manage orders"));
        b.Rest("/orders", r => r
            .Description("Order operations")
            .Consumes("application/json").Produces("application/json")
            .BearerToken("jwt", "JWT")
            .ApiKey("key", "X-API-KEY")
            .Get("/{id}", v => v
                .Id("getOrder")
                .Description("Gets an order")
                .OutType<Order>()
                .Param("id", RestParamType.Path, p => p.DataType("integer").Description("The order id"))
                .Param("expand", RestParamType.Query, p => p.Required(false).AllowableValues("items", "customer").DefaultValue("items"))
                .ResponseMessage(200, "The order", typeof(Order), m => m.Header("X-Version", "Entity version", "integer"))
                .ResponseMessage(404, "Not found", typeof(OrderError))
                .Security("jwt", "orders:read")
                .To("direct:get"))
            .Post(v => v.Type<Order>().OutType<Order>().Param("X-Tenant", RestParamType.Header).Security("key").To("direct:post"))
            .Put("/{id}/lines/{line}", v => v.Type(typeof(List<Order>)).Deprecated().To("direct:put"))
            .Delete("/{id}", v => v.ApiDocs(false).To("direct:delete")));
        b.Rest("/hidden", r => r.ApiDocs(false).Get(v => v.Route(s => s.SetBody(Expr.Constant("x")))));
        foreach (var name in new[] { "get", "post", "put", "delete" })
        {
            b.From("direct:" + name).SetBody(Expr.Constant(null));
        }
    }

    [Fact]
    public async Task Document_is_served_at_the_api_context_path()
    {
        await using var app = await TestApp.StartAsync(Api);

        using var response = await app.Client.GetAsync("/rest/api-doc");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(OpenApiGenerator.OpenApiVersion, (string?)document["openapi"]);
        Assert.Equal("Orders API", (string?)document["info"]!["title"]);
        Assert.Equal("2.1", (string?)document["info"]!["version"]);
        Assert.Equal("Manage orders", (string?)document["info"]!["description"]);
        Assert.Equal("/rest", (string?)document["servers"]![0]!["url"]);
    }

    [Fact]
    public async Task Document_lists_paths_operations_and_parameters()
    {
        await using var app = await TestApp.StartAsync(Api);
        var document = OpenApiGenerator.Generate(app.Context);

        var paths = document["paths"]!.AsObject();
        Assert.Equal(["/orders", "/orders/{id}", "/orders/{id}/lines/{line}"], paths.Select(p => p.Key));
        Assert.Null(paths["/orders/{id}"]!["delete"]);

        var get = paths["/orders/{id}"]!["get"]!;
        Assert.Equal("getOrder", (string?)get["operationId"]);
        Assert.Equal("Gets an order", (string?)get["summary"]);
        Assert.Equal("orders", (string?)get["tags"]![0]);
        var parameters = get["parameters"]!.AsArray();
        Assert.Equal("id", (string?)parameters[0]!["name"]);
        Assert.Equal("path", (string?)parameters[0]!["in"]);
        Assert.True((bool)parameters[0]!["required"]!);
        Assert.Equal("integer", (string?)parameters[0]!["schema"]!["type"]);
        Assert.Equal("expand", (string?)parameters[1]!["name"]);
        Assert.Equal("query", (string?)parameters[1]!["in"]);
        Assert.False((bool)parameters[1]!["required"]!);
        Assert.Equal(["items", "customer"], parameters[1]!["schema"]!["enum"]!.AsArray().Select(v => (string?)v));
        Assert.Equal("items", (string?)parameters[1]!["schema"]!["default"]);
        Assert.Equal("orders:read", (string?)get["security"]![0]!["jwt"]![0]);

        var put = paths["/orders/{id}/lines/{line}"]!["put"]!;
        Assert.True((bool)put["deprecated"]!);
        Assert.Equal(["id", "line"], put["parameters"]!.AsArray().Select(p => (string?)p!["name"]));
        var putSchema = put["requestBody"]!["content"]!["application/json"]!["schema"]!;
        Assert.Equal("array", (string?)putSchema["type"]);
        Assert.Equal("#/components/schemas/Order", (string?)putSchema["items"]!["$ref"]);

        var post = paths["/orders"]!["post"]!;
        Assert.Equal("header", (string?)post["parameters"]![0]!["in"]);
        Assert.True((bool)post["requestBody"]!["required"]!);
        Assert.Equal("#/components/schemas/Order", (string?)post["requestBody"]!["content"]!["application/json"]!["schema"]!["$ref"]);
        Assert.Equal("#/components/schemas/Order", (string?)post["responses"]!["200"]!["content"]!["application/json"]!["schema"]!["$ref"]);
    }

    [Fact]
    public async Task Document_has_responses_schemas_and_security_schemes()
    {
        await using var app = await TestApp.StartAsync(Api);
        var document = OpenApiGenerator.Generate(app.Context);

        var responses = document["paths"]!["/orders/{id}"]!["get"]!["responses"]!;
        Assert.Equal("The order", (string?)responses["200"]!["description"]);
        Assert.Equal("integer", (string?)responses["200"]!["headers"]!["X-Version"]!["schema"]!["type"]);
        Assert.Equal("#/components/schemas/OrderError", (string?)responses["404"]!["content"]!["application/json"]!["schema"]!["$ref"]);

        var schemas = document["components"]!["schemas"]!;
        var order = schemas["Order"]!;
        Assert.Equal(["id", "item", "quantity"], order["properties"]!.AsObject().Select(p => p.Key));
        Assert.Equal("integer", (string?)order["properties"]!["id"]!["type"]);
        Assert.NotNull(schemas["OrderError"]);

        var schemes = document["components"]!["securitySchemes"]!;
        Assert.Equal("http", (string?)schemes["jwt"]!["type"]);
        Assert.Equal("bearer", (string?)schemes["jwt"]!["scheme"]);
        Assert.Equal("JWT", (string?)schemes["jwt"]!["bearerFormat"]);
        Assert.Equal("apiKey", (string?)schemes["key"]!["type"]);
        Assert.Equal("X-API-KEY", (string?)schemes["key"]!["name"]);
        Assert.Equal("header", (string?)schemes["key"]!["in"]);
        Assert.Equal("orders", (string?)document["tags"]![0]!["name"]);
        Assert.Equal("Order operations", (string?)document["tags"]![0]!["description"]);
    }

    [Fact]
    public async Task Removed_routes_leave_the_document()
    {
        await using var app = await TestApp.StartAsync(b => b.Rest("/temp", r => r.Get(v => v.RouteId("temp").Route(s => s.SetBody(Expr.Constant("x"))))));
        Assert.NotNull(OpenApiGenerator.Generate(app.Context)["paths"]!["/temp"]);

        await app.Context.StopRouteAsync("temp");
        await app.Context.RemoveRouteAsync("temp");

        Assert.Empty(OpenApiGenerator.Generate(app.Context)["paths"]!.AsObject());
    }

    [Fact]
    public async Task Without_api_context_path_no_document_is_served_unless_a_path_is_given()
    {
        await using var app = await TestApp.StartAsync(b => b.Rest("/x", r => r.Get(v => v.Route(s => s.SetBody(Expr.Constant("x"))))));
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync("/api-doc")).StatusCode);

        await using var explicitPath = await TestApp.StartAsync(
            c => c.AddRoutes(RouteBuilder.Create(b => b.Rest("/x", r => r.Get(v => v.Route(s => s.SetBody(Expr.Constant("x"))))))),
            o => o.OpenApiPath = "/openapi.json");
        var document = JsonNode.Parse(await explicitPath.Client.GetStringAsync("/openapi.json"))!;
        Assert.NotNull(document["paths"]!["/x"]!["get"]);
        Assert.Equal("Caravan REST API", (string?)document["info"]!["title"]);
    }
}
