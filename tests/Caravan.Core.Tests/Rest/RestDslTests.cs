using System.Text;
using System.Text.Json;
using Caravan.Builder;
using Caravan.Components.Rest;
using Caravan.Model;
using Caravan.Model.Rest;
using Caravan.Processors;
using Caravan.Support;

namespace Caravan.Core.Tests.Rest;

public class RestDslTests : ContextTestBase
{
    private readonly TestRestComponent _rest = new();

    public sealed class Item
    {
        public string? Name { get; set; }

        public int Count { get; set; }
    }

    public override Task InitializeAsync()
    {
        Context.AddComponent("test-rest", _rest);
        Context.GetRestConfiguration().Component = "test-rest";
        return base.InitializeAsync();
    }

    [Fact]
    public void Each_verb_becomes_a_route_from_a_rest_endpoint_with_a_binding_node()
    {
        var builder = (RouteBuilder)RouteBuilder.Create(b => b.Rest("/api/items/", r => r
            .Produces("application/json")
            .Get("/{id}", v => v.Id("getItem").Description("Gets").To("mock:get"))
            .Post(v => v.Consumes("application/xml").EnableCors().Route(s => s.SetBody(Expr.Constant("x")).To("mock:post")))
            .Put("{id}", v => v.Disabled().To("mock:put"))));

        var routes = builder.Build(Context);

        Assert.Equal(["rest:get:/api/items:/{id}?produces=application/json", "rest:post:/api/items?consumes=application/xml&produces=application/json&enableCors=true"],
            routes.Select(r => r.FromUri));
        Assert.Equal("getItem", routes[0].Id);
        Assert.Equal("Gets", routes[0].Description);
        var binding = Assert.IsType<RestBindingDefinition>(Assert.Single(routes[0].Outputs));
        Assert.Equal("GET", binding.Method);
        Assert.Equal("mock:get", Assert.IsType<ToDefinition>(Assert.Single(binding.Outputs)).Uri);
        Assert.Same(binding, binding.Outputs[0].Parent);
        Assert.Same(routes[0], binding.Outputs[0].Route);
        var post = Assert.IsType<RestBindingDefinition>(routes[1].Outputs[0]);
        Assert.Equal(2, post.Outputs.Count);
        Assert.True(post.EnableCors);
        Assert.Same(builder.RestDefinitions[0].Verbs[0], routes[0].Extensions[typeof(VerbDefinition)]);
    }

    [Fact]
    public void A_verb_needs_a_to_or_a_route()
    {
        var builder = RouteBuilder.Create(b => b.Rest("/x", r => r.Get(_ => { })));

        var e = Assert.Throws<ArgumentException>(() => builder.Build(Context));
        Assert.Contains("neither a to nor an inline route", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Services_are_served_by_the_configured_consumer_factory()
    {
        await StartWithRoutes(b =>
        {
            b.RestConfiguration(c => c.ContextPath("/ctx").EndpointProperty("x", "1"));
            b.Rest("/hello", r => r.Get("/{name}", v => v.To("direct:hello")));
            b.From("direct:hello").Transform(Expr.Simple("Hello ${header.name}"));
        });

        var exchange = await _rest.CallAsync(Context, "GET /ctx/hello/{name}", headers: new Dictionary<string, object?> { ["name"] = "Ada" });

        Assert.Equal("Hello Ada", exchange.Message.Body);
        Assert.Single(_rest.Created);
        Assert.Single(Context.Routes);

        await Context.StopRouteAsync(Context.Routes[0].Id);
        Assert.Empty(_rest.Services);
    }

    [Fact]
    public async Task Json_binding_unmarshals_to_the_type_and_marshals_the_reply()
    {
        await StartWithRoutes(b => b.Rest("/items", r => r.BindingMode(RestBindingMode.Json).Post(v => v.Type<Item>().Route(s => s.Process(e =>
        {
            var item = (Item)e.Message.Body!;
            item.Count++;
        })))));

        var exchange = await _rest.CallAsync(Context, "POST /items", "{\"name\":\"a\",\"count\":1}", new Dictionary<string, object?> { ["Content-Type"] = "application/json" });

        Assert.Null(exchange.Exception);
        Assert.Equal("{\"name\":\"a\",\"count\":2}", Encoding.UTF8.GetString((byte[])exchange.Message.Body!));
        Assert.Equal("application/json", exchange.Message.GetHeader(CaravanHeaders.ContentType));
    }

    [Fact]
    public async Task Json_binding_fails_when_the_body_cannot_be_bound_without_validation()
    {
        await StartWithRoutes(b => b.Rest("/items", r => r.BindingMode(RestBindingMode.Json).Post(v => v.Type<Item>().To("mock:never"))));
        ExpectNone("never");

        var exchange = await _rest.CallAsync(Context, "POST /items", "nope", new Dictionary<string, object?> { ["Content-Type"] = "application/json" });

        Assert.IsAssignableFrom<JsonException>(exchange.Exception);
        await Mock("never").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Validation_stops_before_the_service_with_a_status_code()
    {
        await StartWithRoutes(b =>
        {
            b.RestConfiguration(c => c.ClientRequestValidation());
            b.Rest("/v", r => r.Consumes("application/json").Get(v => v.Param("q", RestParamType.Query).To("mock:never")));
        });
        ExpectNone("never");

        var missing = await _rest.CallAsync(Context, "GET /v");
        var unsupported = await _rest.CallAsync(Context, "GET /v", "x", new Dictionary<string, object?> { ["Content-Type"] = "text/csv", ["q"] = "1" });

        Assert.Equal(400, missing.Message.GetHeader(CaravanHeaders.HttpResponseCode));
        Assert.Equal(415, unsupported.Message.GetHeader(CaravanHeaders.HttpResponseCode));
        await Mock("never").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Builder_error_handling_applies_to_rest_routes()
    {
        await StartWithRoutes(b =>
        {
            b.OnException<InvalidOperationException>(o => o.Handled(true).SetBody(Expr.Constant("handled")));
            b.Rest("/err", r => r.Get(v => v.Route(s => s.ThrowException(new InvalidOperationException("x")))));
        });

        var exchange = await _rest.CallAsync(Context, "GET /err");

        Assert.Null(exchange.Exception);
        Assert.Equal("handled", exchange.Message.Body);
    }

    [Fact]
    public async Task Without_a_consumer_factory_the_route_cannot_start()
    {
        await using var context = new CaravanContext();
        context.AddRoutes(RouteBuilder.Create(b => b.Rest("/x", r => r.Get(v => v.To("mock:x")))));

        var e = await Assert.ThrowsAnyAsync<Exception>(() => context.StartAsync());

        Assert.Contains("Cannot find a component to serve REST services", e.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_single_consumer_factory_bean_is_found_in_the_registry()
    {
        await using var context = new CaravanContext();
        var rest = new TestRestComponent();
        context.AddComponent("test-rest", rest);
        context.Registry.Bind("myRest", rest);
        context.AddRoutes(RouteBuilder.Create(b => b.Rest("/x", r => r.Get(v => v.Route(s => s.SetBody(Expr.Constant("ok")))))));
        await context.StartAsync();

        var exchange = await rest.CallAsync(context, "GET /x");

        Assert.Equal("ok", exchange.Message.Body);
    }

    [Fact]
    public void Binding_modes_parse_with_camel_names()
    {
        Assert.Equal(RestBindingMode.JsonXml, RestConfiguration.ParseBindingMode("json_xml"));
        Assert.Equal(RestBindingMode.Off, RestConfiguration.ParseBindingMode("OFF"));
        Assert.Throws<ArgumentException>(() => RestConfiguration.ParseBindingMode("yaml"));
    }

    [Theory]
    [InlineData("application/json", "application/json; charset=utf-8", true)]
    [InlineData("application/json,application/xml", "application/xml", true)]
    [InlineData("application/json", "text/html, */*;q=0.1", true)]
    [InlineData("application/*", "application/json", true)]
    [InlineData("application/json", "text/plain", false)]
    [InlineData(null, "text/plain", true)]
    public void Media_types_match_with_wildcards(string? allowed, string actual, bool matches) =>
        Assert.Equal(matches, MediaTypeHelper.Matches(allowed, actual));

    [Fact]
    public void Rest_component_parses_method_path_and_template()
    {
        var endpoint = Context.GetEndpoint<RestEndpoint>("rest:put:/api/items:/{id}?consumes=application/json&custom=1");

        Assert.Equal("PUT", endpoint.Method);
        Assert.Equal("/api/items", endpoint.Path);
        Assert.Equal("/{id}", endpoint.UriTemplate);
        Assert.Equal("application/json", endpoint.Consumes);
        Assert.Equal("1", endpoint.Parameters["custom"]);
        Assert.Throws<NotSupportedException>(() => endpoint.CreateProducer());
    }

    [Fact]
    public void Rest_routes_are_cloned_with_their_binding()
    {
        var routes = RouteBuilder.Create(b => b.Rest("/c", r => r.Get(v => v.Route(s => s.SetBody(Expr.Constant("x")))))).Build(Context);

        var copy = routes[0].Clone();

        var binding = Assert.IsType<RestBindingDefinition>(copy.Outputs[0]);
        Assert.NotSame(routes[0].Outputs[0], binding);
        Assert.Same(copy, binding.Outputs[0].Route);
    }
}
