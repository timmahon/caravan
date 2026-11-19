using Caravan.Builder;
using Caravan.Model;

namespace Caravan.Core.Tests;

/// <summary>Route configurations (mirrors Camel's RouteConfigurationTest).</summary>
public class RouteConfigurationTests : ContextTestBase
{
    [Fact]
    public async Task Configurations_without_id_apply_to_routes_selecting_none()
    {
        Context.AddRoutes(new ErrorConfigurations());
        await StartWithRoutes(b =>
        {
            b.From("direct:default").ThrowException(new InvalidOperationException("boom"));
            b.From("direct:selected").RouteConfigurationId("audit").ThrowException(new InvalidOperationException("boom"));
        });

        await Template.SendBodyAsync("direct:default", "a");
        var e = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:selected", "b"));

        Assert.IsType<InvalidOperationException>(e.InnerException);
        Assert.Equal(["a"], Mock("default-error").ReceivedBodies);
        Assert.Equal(["b"], Mock("audit").ReceivedBodies);
    }

    [Fact]
    public async Task Routes_select_configurations_by_id_list_and_wildcard_in_order()
    {
        await StartWithRoutes(b =>
        {
            b.RouteConfiguration("first", c => c.OnException<InvalidOperationException>(o => o.Handled(true).To("mock:first")));
            b.RouteConfiguration("second", c => c.OnException<InvalidOperationException>(o => o.Handled(true).To("mock:second")));
            b.RouteConfiguration("retry-a", c => c.Intercept(i => i.To("mock:retry-a")));
            b.RouteConfiguration("retry-b", c => c.Intercept(i => i.To("mock:retry-b")));
            b.From("direct:one").RouteConfigurationId("first, second").ThrowException(new InvalidOperationException("x"));
            b.From("direct:two").RouteConfigurationId("second,first").ThrowException(new InvalidOperationException("x"));
            b.From("direct:three").RouteConfigurationId("retry-*").To("mock:three");
        });

        await Template.SendBodyAsync("direct:one", 1);
        await Template.SendBodyAsync("direct:two", 2);
        await Template.SendBodyAsync("direct:three", 3);

        Assert.Equal([1], Mock("first").ReceivedBodies);
        Assert.Equal([2], Mock("second").ReceivedBodies);
        Assert.Equal(1, Mock("retry-a").ReceivedCounter);
        Assert.Equal(1, Mock("retry-b").ReceivedCounter);
        var route = (Impl.Route)Context.Routes.Single(r => r.Endpoint.EndpointUri == "direct:three");
        Assert.Equal(["retry-a", "retry-b"], route.RouteContext.RouteConfigurations.Select(c => c.Id));
    }

    [Fact]
    public async Task Route_and_builder_on_exception_take_precedence_over_configurations()
    {
        await StartWithRoutes(b =>
        {
            b.RouteConfiguration(c => c.OnException<InvalidOperationException>(o => o.Handled(true).To("mock:config")));
            b.OnException<InvalidOperationException>(o => o.Handled(true).To("mock:builder"));
            b.From("direct:builder").ThrowException(new InvalidOperationException("x"));
            b.From("direct:route")
                .OnException<InvalidOperationException>(o => o.Handled(true).To("mock:route"))
                .ThrowException(new InvalidOperationException("x"));
        });

        await Template.SendBodyAsync("direct:builder", "b");
        await Template.SendBodyAsync("direct:route", "r");

        Assert.Equal(["b"], Mock("builder").ReceivedBodies);
        Assert.Equal(["r"], Mock("route").ReceivedBodies);
        Assert.Equal(0, Mock("config").ReceivedCounter);
    }

    [Fact]
    public async Task Configuration_on_exception_handles_what_the_route_does_not()
    {
        await StartWithRoutes(b =>
        {
            b.RouteConfiguration(c => c.OnException<ArgumentException>(o => o.Handled(true).To("mock:config")));
            b.OnException<InvalidOperationException>(o => o.Handled(true).To("mock:builder"));
            b.From("direct:start").ThrowException(new ArgumentNullException("x"));
        });

        await Template.SendBodyAsync("direct:start", "a");

        Assert.Equal(["a"], Mock("config").ReceivedBodies);
    }

    [Fact]
    public async Task Configuration_error_handler_applies_unless_the_route_or_builder_sets_one()
    {
        Context.AddRoutes(RouteBuilder.Create(b =>
        {
            b.RouteConfiguration(c => c.ErrorHandler(new DeadLetterChannelDefinition("mock:config-dead")));
            b.RouteConfiguration("other", c => c.ErrorHandler(new DeadLetterChannelDefinition("mock:other-dead")));
            b.From("direct:default").ThrowException(new InvalidOperationException("x"));
            b.From("direct:other").RouteConfigurationId("other").ThrowException(new InvalidOperationException("x"));
            b.From("direct:own").ErrorHandler(new DeadLetterChannelDefinition("mock:own-dead")).ThrowException(new InvalidOperationException("x"));
        }));
        Context.AddRoutes(RouteBuilder.Create(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:builder-dead"));
            b.From("direct:builder").ThrowException(new InvalidOperationException("x"));
        }));
        await Context.StartAsync();

        foreach (var name in new[] { "default", "other", "own", "builder" })
        {
            await Template.SendBodyAsync("direct:" + name, name);
        }

        Assert.Equal(["default"], Mock("config-dead").ReceivedBodies);
        Assert.Equal(["other"], Mock("other-dead").ReceivedBodies);
        Assert.Equal(["own"], Mock("own-dead").ReceivedBodies);
        Assert.Equal(["builder"], Mock("builder-dead").ReceivedBodies);
    }

    [Fact]
    public async Task Configuration_on_completion_is_used_only_by_routes_without_their_own()
    {
        await StartWithRoutes(b =>
        {
            b.RouteConfiguration(c => c.OnCompletion(o => o.To("mock:config-done")));
            b.From("direct:plain").To("mock:plain");
            b.From("direct:own").OnCompletion(o => o.To("mock:own-done")).To("mock:own");
        });

        await Template.SendBodyAsync("direct:plain", "p");
        await Template.SendBodyAsync("direct:own", "o");

        Assert.Equal(["p"], Mock("config-done").ReceivedBodies);
        Assert.Equal(["o"], Mock("own-done").ReceivedBodies);
    }

    [Fact]
    public async Task Configuration_interceptors_apply_to_the_routes_using_it()
    {
        await StartWithRoutes(b =>
        {
            b.RouteConfiguration("audit", c => c
                .Description("auditing")
                .InterceptFrom("direct:*", i => i.To("mock:entered"))
                .Intercept(i => i.To("mock:step"))
                .InterceptSendToEndpoint("mock:target", i => i.To("mock:before-target")));
            b.Intercept(i => i.To("mock:builder-step"));
            b.From("direct:audited").RouteConfigurationId("audit").To("mock:target");
            b.From("direct:plain").To("mock:plain");
        });

        await Template.SendBodyAsync("direct:audited", "a");
        await Template.SendBodyAsync("direct:plain", "p");

        Assert.Equal(["a"], Mock("entered").ReceivedBodies);
        Assert.Equal(["a"], Mock("step").ReceivedBodies);
        Assert.Equal(["a", "p"], Mock("builder-step").ReceivedBodies);
        Assert.Equal(["a"], Mock("before-target").ReceivedBodies);
        Assert.Equal("auditing", Assert.Single(Context.RouteConfigurationDefinitions).Description);
    }

    [Fact]
    public async Task Unknown_configuration_id_fails_route_creation_but_patterns_may_match_nothing()
    {
        Context.AddRoutes(RouteBuilder.Create(b =>
        {
            b.From("direct:pattern").RouteConfigurationId("nothing-*").To("mock:pattern");
            b.From("direct:missing").RouteId("missing").RouteConfigurationId("nope").To("mock:missing");
        }));

        var e = await Assert.ThrowsAsync<FailedToCreateRouteException>(() => Context.StartAsync());

        Assert.Equal("missing", e.RouteId);
        Assert.Contains("Route configuration with id nope not found", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Configurations_added_after_routes_apply_when_routes_are_created()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.From("direct:start").RouteConfigurationId("late").ThrowException(new InvalidOperationException("x"))));
        Context.AddRouteConfiguration(new RouteConfigurationDefinition
        {
            Id = "late",
            ErrorHandler = new DeadLetterChannelDefinition("mock:late-dead"),
        });
        await Context.StartAsync();

        await Template.SendBodyAsync("direct:start", "a");

        Assert.Equal(["a"], Mock("late-dead").ReceivedBodies);
        Assert.Single(Context.RouteConfigurationDefinitions);
    }

    [Fact]
    public async Task Template_routes_use_the_configurations_they_select()
    {
        Context.AddRoutes(new ErrorConfigurations());
        Context.AddRoutes(RouteBuilder.Create(b => b.RouteTemplate("t")
            .TemplateParameter("name")
            .TemplateParameter("cfg")
            .From("direct:{{name}}")
            .RouteConfigurationId("{{cfg}}")
            .ThrowException(new InvalidOperationException("x"))));
        await Context.StartAsync();
        await Context.AddRouteFromTemplateAsync("a", "t", new Dictionary<string, object?> { ["name"] = "a", ["cfg"] = "audit" });

        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:a", "x"));

        Assert.Equal("audit", Context.RouteDefinitions.Single().RouteConfigurationIds);
        Assert.Equal(1, Mock("audit").ReceivedCounter);
    }

    private sealed class ErrorConfigurations : RouteConfigurationBuilder
    {
        public override void Configuration()
        {
            RouteConfiguration(c => c.OnException<InvalidOperationException>(o => o.Handled(true).To("mock:default-error")));
            RouteConfiguration("audit", c => c.Intercept(i => i.To("mock:audit")));
        }
    }
}
