using Caravan.Builder;
using static Caravan.Builder.Lang;

namespace Caravan.Core.Tests;

/// <summary>Route <c>precondition</c>, <c>nodePrefixId</c>, templated route <c>prefixId</c> and route configuration preconditions.</summary>
public class RouteOptionsTests : ContextTestBase
{
    [Fact]
    public async Task Routes_whose_precondition_is_false_are_not_added()
    {
        Context.PropertiesComponent.InitialProperties["feature.a"] = "true";
        Context.PropertiesComponent.InitialProperties["feature.b"] = "false";
        Context.PropertiesComponent.InitialProperties["env"] = "prod";
        await StartWithRoutes(b =>
        {
            b.From("direct:a").RouteId("a").Precondition("{{feature.a}}").To("mock:a");
            b.From("direct:b").RouteId("b").Precondition("{{feature.b}}").To("mock:b");
            b.From("direct:c").RouteId("c").Precondition("${properties:env} == 'prod'").To("mock:c");
            b.From("direct:d").RouteId("d").Precondition("${properties:env:dev} == 'dev'").To("mock:d");
            b.From("direct:e").RouteId("e").Precondition("{{?missing}}").To("mock:e");
        });

        Assert.Equal(["a", "c"], Context.RouteDefinitions.Select(r => r.Id).Order());
        await Template.SendBodyAsync("direct:a", "x");
        Assert.Single(Mock("a").ReceivedExchanges);
    }

    [Fact]
    public async Task Node_prefix_id_prefixes_explicit_and_generated_node_ids()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:a").RouteId("a").NodePrefixId("p-").To("mock:a").Id("send").Choice(c => c.When(Simple("${body} == 'x'"), w => w.Log("x")));
            b.From("direct:b").RouteId("b").To("mock:b").Id("send");
        });

        var route = Context.RouteDefinitions.Single(r => r.Id == "a");
        Assert.All(route.AllNodes(), n => Assert.StartsWith("p-", n.Id));
        Assert.Equal("p-send", route.Outputs[0].Id);
        Assert.Equal("send", Context.RouteDefinitions.Single(r => r.Id == "b").Outputs[0].Id);
        Assert.Equal("a", route.Id);
        await Template.SendBodyAsync("direct:a", "x");
        Assert.Single(Mock("a").ReceivedExchanges);
    }

    [Fact]
    public async Task Templated_routes_with_a_prefix_id_have_unique_node_ids()
    {
        await StartWithRoutes(b =>
        {
            b.RouteTemplate("t").TemplateParameter("name").From("direct:{{name}}").To("mock:{{name}}").Id("send");
            b.TemplatedRoute("t", r => r.RouteId("one").Parameter("name", "one").PrefixId("one-"));
            b.TemplatedRoute("t", r => r.RouteId("two").Parameter("name", "two").PrefixId("two-"));
        });

        Assert.Equal("one-send", Context.RouteDefinitions.Single(r => r.Id == "one").Outputs[0].Id);
        Assert.Equal("two-send", Context.RouteDefinitions.Single(r => r.Id == "two").Outputs[0].Id);
        await Template.SendBodyAsync("direct:two", "x");
        Assert.Single(Mock("two").ReceivedExchanges);
    }

    [Fact]
    public async Task Route_configurations_whose_precondition_is_false_are_not_added()
    {
        Context.PropertiesComponent.InitialProperties["dlc"] = "false";
        await StartWithRoutes(b =>
        {
            b.RouteConfiguration("on", c => c.Precondition("true").OnException<InvalidOperationException>(o => o.Handled(true).To("mock:on")));
            b.RouteConfiguration("off", c => c.Precondition("{{dlc}}").OnException<InvalidOperationException>(o => o.Handled(true).To("mock:off")));
            b.From("direct:start").RouteConfigurationId("on, off*").Process(_ => throw new InvalidOperationException("boom"));
        });

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(["on"], Context.RouteConfigurationDefinitions.Select(c => c.Id));
        Assert.Single(Mock("on").ReceivedExchanges);
        Assert.Empty(Mock("off").ReceivedExchanges);
    }

    [Fact]
    public async Task An_invalid_precondition_fails_when_the_route_is_added()
    {
        await Assert.ThrowsAnyAsync<Exception>(() =>
            StartWithRoutes(b => b.From("direct:a").Precondition("${unknownFunction}").To("mock:a")));
    }
}
