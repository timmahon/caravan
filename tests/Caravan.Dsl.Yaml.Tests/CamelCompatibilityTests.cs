namespace Caravan.Dsl.Yaml.Tests;

/// <summary>Camel YAML DSL files (in <c>Samples/</c>) in the shapes found in Camel's documentation and examples, loaded unchanged.</summary>
public class CamelCompatibilityTests : YamlTestBase
{
    public static string SamplePath(string name) => Path.Combine(AppContext.BaseDirectory, "Samples", name);

    private async Task StartFileAsync(string name)
    {
        Context.AddRoutes(YamlRoutesBuilder.FromFile(SamplePath(name)));
        await Context.StartAsync();
    }

    [Fact]
    public async Task Hello_world_timer_route()
    {
        Mock("hello").ExpectedBodiesReceived("Hello Camel from hello");

        await StartFileAsync("camel-hello.yaml");

        await Mock("hello").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Content_based_router_with_expression_wrappers()
    {
        await StartFileAsync("camel-content-based-router.yaml");
        Mock("widget").ExpectedBodiesReceived("w");
        Mock("gadget").ExpectedBodiesReceived("g");
        Mock("other").ExpectedBodiesReceived("o");

        await Template.SendBodyAndHeaderAsync("direct:orders", "w", "type", "widget");
        await Template.SendBodyAndHeaderAsync("direct:orders", "g", "type", "gadget");
        await Template.SendBodyAndHeaderAsync("direct:orders", "o", "type", "other");

        await Components.Mock.MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Kamelet_style_template()
    {
        await StartFileAsync("camel-kamelet-template.yaml");
        Mock("prefixed").ExpectedBodiesReceived("Hi Bob");

        await Template.SendBodyAsync("direct:kamelet", "Bob");

        await Mock("prefixed").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Route_configuration_with_dead_letter_channel_and_on_exception()
    {
        await StartFileAsync("camel-error-handling.yaml");
        Mock("invalid").ExpectedBodiesReceived("invalid input");
        Mock("dead").ExpectedBodiesReceived("boom");
        Mock("ok").ExpectedBodiesReceived("fine");

        await Template.SendBodyAsync("direct:guarded", "bad");
        await Template.SendBodyAsync("direct:guarded", "boom");
        await Template.SendBodyAsync("direct:guarded", "fine");

        await Components.Mock.MockEndpoint.AssertIsSatisfiedAsync(Context);
        Assert.Equal(3, Mock("dead").ReceivedExchanges[0].Message.GetHeader(CaravanHeaders.RedeliveryCounter));
    }

    [Fact]
    public async Task Split_and_aggregate_with_beans_and_kebab_case()
    {
        await StartFileAsync("camel-split-aggregate.yaml");
        Mock("aggregated").ExpectedBodiesReceivedInAnyOrder("a1+a2", "b1+b2");

        await Template.SendBodyAsync("direct:lines", "a1,b1,a2,b2");

        await Mock("aggregated").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Orders_sample()
    {
        await StartFileAsync("orders.yaml");
        Mock("big").ExpectedBodiesReceived("b");
        Mock("small").ExpectedBodiesReceived("s");

        await Template.SendBodyAndHeaderAsync("direct:orders", "b", "total", 150);
        await Template.SendBodyAndHeaderAsync("direct:orders", "s", "total", 15);

        await Mock("big").AssertIsSatisfiedAsync();
        await Mock("small").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Eip_showcase_creates_every_step_and_runs()
    {
        Context.AddRoutes(YamlRoutesBuilder.FromFile(SamplePath("eip-showcase.yaml")));
        await Context.StartAsync();
        await Template.SendBodyAsync("seda:poll", "polled");

        var exchange = await Template.SendAsync("direct:showcase", e =>
        {
            e.Message.Body = "x";
            e.Message.SetHeader("name", "dynamic");
            e.Message.SetHeader("id", 1);
            e.Message.SetHeader("seq", 1);
        });

        Assert.Null(exchange.Exception);
        var showcase = Context.RouteDefinitions.Single(r => r.Id == "showcase");
        var stepNames = showcase.AllNodes().Select(o => o.ShortName).Concat(["onException", "onCompletion"]).ToHashSet();
        var mapped = Caravan.Dsl.Yaml.Catalog.StepCatalog.Steps.Keys.Where(k => k is not ("inOnly" or "inOut" or "kamelet")).ToList();
        Assert.All(mapped, name => Assert.Contains(name, stepNames));
    }
}
