using Caravan.Builder;

namespace Caravan.Testing.Tests;

/// <summary>AdviceWith on routes created from templates: each route is a copy, so advice changes only that route.</summary>
public class RouteTemplateAdviceWithTests : ContextTestBase
{
    private void AddTemplate() => AddRoutes(b => b.RouteTemplate("t")
        .TemplateParameter("name")
        .From("direct:{{name}}")
        .Transform(e => e.Message.GetBody<string>() + ">t").Id("transform")
        .To("mock:{{name}}").Id("send"));

    [Fact]
    public async Task Advising_one_route_leaves_the_template_and_the_other_routes_unchanged()
    {
        AddTemplate();
        await Context.AddRouteFromTemplateAsync("a", "t", new Dictionary<string, object?> { ["name"] = "a" });
        await Context.AddRouteFromTemplateAsync("b", "t", new Dictionary<string, object?> { ["name"] = "b" });

        await Context.AdviceWithAsync("a", a =>
        {
            a.WeaveById("send").Replace(r => r.To("mock:advised"));
            a.WeaveAddFirst().To("mock:first");
        });
        await Context.StartAsync();
        await Template.SendBodyAsync("direct:a", "x");
        await Template.SendBodyAsync("direct:b", "y");

        Assert.Equal(["x>t"], Mock("advised").ReceivedBodies);
        Assert.Equal(1, Mock("first").ReceivedCounter);
        Assert.Equal(0, Mock("a").ReceivedCounter);
        Assert.Equal(["y>t"], Mock("b").ReceivedBodies);
        Assert.Equal(2, Context.RouteTemplateDefinitions.Single().Route!.Outputs.Count);
        Assert.Equal(2, Context.RouteDefinitions.Single(r => r.Id == "b").Outputs.Count);

        // A route created afterwards from the same template is not advised either.
        await Context.AddRouteFromTemplateAsync("c", "t", new Dictionary<string, object?> { ["name"] = "c" });
        await Template.SendBodyAsync("direct:c", "z");
        Assert.Equal(["z>t"], Mock("c").ReceivedBodies);
    }

    [Fact]
    public async Task Advising_a_started_templated_route_keeps_its_local_beans_and_parameters()
    {
        AddRoutes(b => b.RouteTemplate("beans")
            .TemplateParameter("name")
            .TemplateBean("suffix", rtc => new Suffix(">" + rtc.GetProperty("name")))
            .From("direct:{{name}}")
            .Bean("suffix", "Apply").Id("bean")
            .To("mock:{{name}}"));
        await Context.StartAsync();
        await Context.AddRouteFromTemplateAsync("a", "beans", new Dictionary<string, object?> { ["name"] = "a" });

        await Context.AdviceWithAsync("a", a => a.WeaveById("bean").After(r => r.To("mock:after")));
        await Template.SendBodyAsync("direct:a", "x");

        Assert.Equal(["x>a"], Mock("after").ReceivedBodies);
        Assert.Equal(["x>a"], Mock("a").ReceivedBodies);
        Assert.Equal("beans", Context.RouteDefinitions.Single().TemplateId);
    }

    public sealed class Suffix(string suffix)
    {
        public string Apply(string body) => body + suffix;
    }
}
