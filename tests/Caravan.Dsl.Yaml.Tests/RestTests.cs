using Caravan.Model.Rest;

namespace Caravan.Dsl.Yaml.Tests;

public class RestTests : YamlTestBase
{
    private RouteBuilderResult Load(string yaml)
    {
        var builder = YamlRoutesBuilder.FromString(yaml, "test.yaml");
        var routes = builder.Build(Context);
        return new RouteBuilderResult(builder.RestDefinitions, routes);
    }

    [Fact]
    public void Camel_rest_sample_loads_into_rest_definitions_and_routes()
    {
        var builder = YamlRoutesBuilder.FromFile(CamelCompatibilityTests.SamplePath("camel-rest.yaml"));
        var routes = builder.Build(Context);

        var configuration = Context.GetRestConfiguration();
        Assert.Equal("platform-http", configuration.Component);
        Assert.Equal(RestBindingMode.Json, configuration.BindingMode);
        Assert.Equal("/api", configuration.ContextPath);
        Assert.Equal("/api-doc", configuration.ApiContextPath);
        Assert.True(configuration.EnableCors);
        Assert.Equal("User API", configuration.ApiProperties["api.title"]);
        Assert.Equal("true", configuration.DataFormatProperties["prettyPrint"]);

        var rest = Assert.Single(builder.RestDefinitions);
        Assert.Equal("/users", rest.Path);
        Assert.Equal(["basic", "api_key"], rest.SecurityDefinitions.Select(s => s.Key));
        Assert.Equal(RestSecurityType.ApiKey, rest.SecurityDefinitions[1].Type);
        Assert.Equal("header", rest.SecurityDefinitions[1].In);
        Assert.Equal(["get", "get", "put"], rest.Verbs.Select(v => v.Method));

        var get = rest.Verbs[0];
        Assert.Equal("/{id}", get.Path);
        Assert.Equal(typeof(TestUser), get.OutType);
        Assert.Equal("integer", get.Params[0].DataType);
        Assert.Equal(["200", "404"], get.ResponseMessages.Select(m => m.Code));
        Assert.Equal("basic", get.Security[0].Key);
        Assert.Equal(typeof(TestUser[]), rest.Verbs[1].OutType);
        Assert.Equal(["active", "inactive"], rest.Verbs[1].Params[0].AllowableValues);
        Assert.False(rest.Verbs[1].Params[0].Required);
        Assert.Equal("direct:findAll", rest.Verbs[1].To);
        Assert.Equal(RestParamType.Body, rest.Verbs[2].Params[0].Type);

        // direct:getUser is inlined into its service; the other services keep their to.
        Assert.Equal(
            [
                "rest:get:/users:/findAll?consumes=application/json&produces=application/json",
                "rest:get:/users:/{id}?consumes=application/json&produces=application/json",
                "rest:put:/users:/{id}?consumes=application/json&produces=application/json",
            ],
            routes.Select(r => r.FromUri).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(routes, r => r.FromUri == "direct:getUser");
    }

    [Fact]
    public void Verbs_take_steps_inline_and_rest_options()
    {
        var result = Load("""
            - rest:
                path: /x
                bindingMode: json_xml
                clientRequestValidation: true
                enableCors: false
                apiDocs: false
                tag: things
                delete:
                  - path: "/{id}"
                    routeId: del
                    deprecated: true
                    bindingMode: off
                    skipBindingOnErrorCode: false
                    steps:
                      - setBody:
                          constant: deleted
            """);

        var rest = Assert.Single(result.Rests);
        Assert.Equal(RestBindingMode.JsonXml, rest.BindingMode);
        Assert.True(rest.ClientRequestValidation);
        Assert.False(rest.EnableCors);
        Assert.False(rest.ApiDocs);
        Assert.Equal("things", rest.Tag);
        var verb = rest.Verbs[0];
        Assert.True(verb.Deprecated);
        Assert.Equal(RestBindingMode.Off, verb.BindingMode);
        Assert.False(verb.SkipBindingOnErrorCode);
        var route = Assert.Single(result.Routes);
        Assert.Equal("del", route.Id);
        var binding = Assert.IsType<RestBindingDefinition>(route.Outputs[0]);
        Assert.Single(binding.Outputs);
    }

    [Fact]
    public async Task Inline_services_run_through_a_consumer_factory()
    {
        var rest = new Rest.YamlTestRestFactory();
        Context.AddComponent("yaml-rest", rest);
        await StartAsync("""
            - restConfiguration:
                component: yaml-rest
            - rest:
                get:
                  - path: /hi
                    steps:
                      - setBody:
                          simple: "hi ${header.who}"
            """);

        Assert.Equal("hi joe", await rest.CallAsync(Context, "GET /hi", "joe"));
    }

    [Theory]
    [InlineData("- rest: { path: /a, get: [ { path: /b } ] }", "needs either to or steps")]
    [InlineData("- rest: { path: /a, get: [ { to: direct:a, steps: [ { log: x } ] } ] }", "needs either to or steps")]
    [InlineData("- rest: { path: /a, get: [ { to: direct:a, parm: [] } ] }", "Unknown option 'parm'")]
    [InlineData("- rest: { path: /a, get: [ { to: direct:a, param: [ { name: x, type: cookie } ] } ] }", "Option 'type' of 'param' must be one of path, query, header, body, formData")]
    [InlineData("- rest: { path: /a, get: [ { to: direct:a, type: No.Such.Type } ] }", "Cannot resolve type 'No.Such.Type'")]
    [InlineData("- rest: { path: /a, securityDefinitions: { oauth2: [] } }", "only basicAuth, bearerToken and apiKey security definitions are supported")]
    [InlineData("- restConfiguration: { bindingMode: yaml }", "Option 'bindingMode' of 'restConfiguration' must be one of off, auto, json, xml, json_xml")]
    [InlineData("- restConfiguration: { apiProperty: [ { key: a } ] }", "Missing required option 'value'")]
    public void Invalid_rest_definitions_have_precise_messages(string yaml, string expected)
    {
        var error = LoadError(yaml);

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        Assert.StartsWith("test.yaml:1:", error.Message, StringComparison.Ordinal);
    }

    private sealed record RouteBuilderResult(IReadOnlyList<RestDefinition> Rests, IReadOnlyList<Caravan.Model.RouteDefinition> Routes);
}
