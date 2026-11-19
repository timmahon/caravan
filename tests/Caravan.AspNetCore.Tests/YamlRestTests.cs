using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Caravan.AspNetCore.OpenApi;
using Caravan.Dsl.Yaml;

namespace Caravan.AspNetCore.Tests;

public class YamlRestTests
{
    private const string Yaml = """
        - restConfiguration:
            bindingMode: json
            contextPath: /yaml
            apiContextPath: /openapi
            clientRequestValidation: true
            apiProperty:
              - key: api.title
                value: YAML orders
        - rest:
            path: /orders
            tag: orders
            produces: application/json
            get:
              - path: "/{id}"
                id: getOrder
                outType: Caravan.AspNetCore.Tests.Order
                param:
                  - name: id
                    type: path
                    dataType: integer
                to: "direct:getOrder"
            post:
              - type: Caravan.AspNetCore.Tests.Order
                consumes: application/json
                param:
                  - name: body
                    type: body
                responseMessage:
                  - code: "201"
                    message: Created
                    responseModel: Caravan.AspNetCore.Tests.Order
                to:
                  uri: direct:createOrder
            delete:
              - path: "/{id}"
                steps:
                  - setHeader:
                      name: CaravanHttpResponseCode
                      constant: 204
                  - setBody:
                      constant: ""
        - route:
            id: get-order
            from:
              uri: direct:getOrder
              steps:
                - setBody:
                    simple: '{"id": ${header.id}, "item": "yaml"}'
                - unmarshal:
                    json:
                      unmarshalType: Caravan.AspNetCore.Tests.Order
        - from:
            uri: direct:createOrder
            steps:
              - setHeader:
                  name: CaravanHttpResponseCode
                  constant: 201
        """;

    [Fact]
    public async Task Yaml_rest_services_are_served_with_binding()
    {
        await using var app = await TestApp.StartAsync(c => c.AddRoutes(YamlRoutesBuilder.FromString(Yaml, "orders.yaml")));

        var order = await app.Client.GetFromJsonAsync<JsonObject>("/yaml/orders/9");
        Assert.Equal(9, (int)order!["id"]!);
        Assert.Equal("yaml", (string?)order["item"]);

        using var created = await app.Client.PostAsync("/yaml/orders", new StringContent("{\"item\":\"pen\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("pen", (string?)JsonNode.Parse(await created.Content.ReadAsStringAsync())!["item"]);

        using var invalid = await app.Client.PostAsync("/yaml/orders", new StringContent("x", Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, invalid.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await app.Client.DeleteAsync("/yaml/orders/9")).StatusCode);
        var ids = app.Context.RouteDefinitions.Select(r => r.Id).ToList();
        Assert.Contains("getOrder", ids);
        Assert.DoesNotContain("get-order", ids);
        Assert.Equal(3, ids.Count);
    }

    [Fact]
    public async Task Yaml_rest_services_are_documented()
    {
        await using var app = await TestApp.StartAsync(c => c.AddRoutes(YamlRoutesBuilder.FromString(Yaml, "orders.yaml")));

        var document = JsonNode.Parse(await app.Client.GetStringAsync("/yaml/openapi"))!;

        Assert.Equal("YAML orders", (string?)document["info"]!["title"]);
        Assert.Equal("getOrder", (string?)document["paths"]!["/orders/{id}"]!["get"]!["operationId"]);
        Assert.Equal("integer", (string?)document["paths"]!["/orders/{id}"]!["get"]!["parameters"]![0]!["schema"]!["type"]);
        Assert.Equal("Created", (string?)document["paths"]!["/orders"]!["post"]!["responses"]!["201"]!["description"]);
        Assert.NotNull(document["components"]!["schemas"]!["Order"]);
        Assert.Equal(OpenApiGenerator.OpenApiVersion, (string?)document["openapi"]);
    }
}
