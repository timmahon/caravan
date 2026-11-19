using Caravan.Builder;
using Caravan.Components.Mock;
using Caravan.Model;
using Caravan.Spi;

namespace Caravan.Testing.Tests;

/// <summary>AdviceWith (mirrors Camel's AdviceWith*Test).</summary>
public class AdviceWithTests : ContextTestBase
{
    private void AddOrderRoute() => AddRoutes(b => b.From("direct:start").RouteId("orders")
        .SetBody(e => e.Message.GetBody<string>() + ">set").Id("set")
        .To("mock:a").Id("a")
        .To("mock:b").Id("b")
        .To("mock:result"));

    [Fact]
    public async Task WeaveById_replace_before_the_context_starts()
    {
        AddOrderRoute();

        await AdviceWith.RouteAsync(Context, "orders", a => a.WeaveById("a").Replace(r => r.To("mock:replaced")));
        await Context.StartAsync();
        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(0, Mock("a").ReceivedCounter);
        Assert.Equal(1, Mock("replaced").ReceivedCounter);
        Assert.Equal(1, Mock("b").ReceivedCounter);
    }

    [Fact]
    public async Task WeaveById_remove_before_and_after()
    {
        AddOrderRoute();

        await Context.AdviceWithAsync("orders", a =>
        {
            a.WeaveById("a").Remove();
            a.WeaveById("b").Before(r => r.Transform(e => e.Message.GetBody<string>() + ">before"));
            a.WeaveById("b").After(r => r.Transform(e => e.Message.GetBody<string>() + ">after"));
        });
        await Context.StartAsync();
        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(0, Mock("a").ReceivedCounter);
        Assert.Equal(["x>set>before"], Mock("b").ReceivedBodies);
        Assert.Equal(["x>set>before>after"], Mock("result").ReceivedBodies);
    }

    [Fact]
    public async Task WeaveById_accepts_wildcards()
    {
        AddRoutes(b => b.From("direct:start").RouteId("r").To("mock:a").Id("send-a").To("mock:b").Id("send-b").To("mock:c").Id("other"));

        await AdviceWith.RouteAsync(Context, "r", a => a.WeaveById("send-*").Remove());
        await Context.StartAsync();
        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(0, Mock("a").ReceivedCounter + Mock("b").ReceivedCounter);
        Assert.Equal(1, Mock("c").ReceivedCounter);
    }

    [Theory]
    [InlineData("first", new[] { "a2", "a3", "replaced" })]
    [InlineData("last", new[] { "a1", "a2", "replaced" })]
    [InlineData("index", new[] { "a1", "a3", "replaced" })]
    [InlineData("range", new[] { "a3", "replaced", "replaced" })]
    [InlineData("all", new[] { "replaced", "replaced", "replaced" })]
    public async Task WeaveByToUri_with_selectors(string selector, string[] expected)
    {
        AddRoutes(b => b.From("direct:start").RouteId("r").To("mock:a1").To("mock:a2").To("mock:a3"));

        await AdviceWith.RouteAsync(Context, "r", a =>
        {
            var weave = a.WeaveByToUri("mock:a*");
            weave = selector switch
            {
                "first" => weave.SelectFirst(),
                "last" => weave.SelectLast(),
                "index" => weave.SelectIndex(1),
                "range" => weave.SelectRange(0, 1),
                _ => weave,
            };
            weave.Replace(r => r.To("mock:replaced"));
        });
        await Context.StartAsync();
        await Template.SendBodyAsync("direct:start", "x");

        var received = new[] { "a1", "a2", "a3", "replaced" }
            .SelectMany(name => Enumerable.Repeat(name, Mock(name).ReceivedCounter))
            .Order();
        Assert.Equal(expected.Order(), received);
    }

    [Fact]
    public async Task WeaveByType_and_WeaveByToString()
    {
        AddRoutes(b => b.From("direct:start").RouteId("r").SetBody(_ => "changed").To("mock:a").To("mock:b"));

        await AdviceWith.RouteAsync(Context, "r", a =>
        {
            a.WeaveByType<SetBodyDefinition>().Remove();
            a.WeaveByToString("to[mock:b]").Replace(r => r.To("mock:c"));
        });
        await Context.StartAsync();
        await Template.SendBodyAsync("direct:start", "original");

        Assert.Equal(["original"], Mock("a").ReceivedBodies);
        Assert.Equal(0, Mock("b").ReceivedCounter);
        Assert.Equal(["original"], Mock("c").ReceivedBodies);
    }

    [Fact]
    public async Task Weaves_inside_choice_and_split_blocks()
    {
        AddRoutes(b => b.From("direct:start").RouteId("r")
            .Choice(c => c
                .When(e => e.Message.GetBody<string>() == "gold", r => r.To("mock:gold").Id("gold"))
                .Otherwise(r => r.To("mock:other")))
            .Split(Expr.Constant(new[] { 1, 2, 3 }), s => s.To("mock:part")));

        await AdviceWith.RouteAsync(Context, "r", a =>
        {
            a.WeaveById("gold").Replace(r => r.To("mock:vip"));
            a.WeaveByToUri("mock:part").After(r => r.To("mock:afterPart"));
            a.WeaveByType<OtherwiseDefinition>().Remove();
        });
        await Context.StartAsync();
        await Template.SendBodyAsync("direct:start", "gold");
        await Template.SendBodyAsync("direct:start", "silver");

        Assert.Equal(0, Mock("gold").ReceivedCounter);
        Assert.Equal(1, Mock("vip").ReceivedCounter);
        Assert.Equal(0, Mock("other").ReceivedCounter);
        Assert.Equal(6, Mock("part").ReceivedCounter);
        Assert.Equal(6, Mock("afterPart").ReceivedCounter);
    }

    [Fact]
    public async Task MaxDeep_limits_the_depth_of_matches()
    {
        AddRoutes(b => b.From("direct:start").RouteId("r")
            .To("mock:top")
            .Filter(_ => true, f => f.To("mock:nested")));

        await AdviceWith.RouteAsync(Context, "r", a => a.WeaveByType<ToDefinition>().MaxDeep(1).Replace(r => r.To("mock:replaced")));
        await Context.StartAsync();
        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(0, Mock("top").ReceivedCounter);
        Assert.Equal(1, Mock("replaced").ReceivedCounter);
        Assert.Equal(1, Mock("nested").ReceivedCounter);
    }

    [Fact]
    public async Task WeaveAddFirst_and_WeaveAddLast()
    {
        AddRoutes(b => b.From("direct:start").RouteId("r").Transform(e => e.Message.GetBody<string>() + ">route"));

        await AdviceWith.RouteAsync(Context, "r", a =>
        {
            a.WeaveAddFirst().Transform(e => e.Message.GetBody<string>() + ">first").To("mock:first");
            a.WeaveAddLast(r => r.Transform(e => e.Message.GetBody<string>() + ">last").To("mock:last"));
        });
        await Context.StartAsync();
        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(["x>first"], Mock("first").ReceivedBodies);
        Assert.Equal(["x>first>route>last"], Mock("last").ReceivedBodies);
    }

    [Fact]
    public async Task ReplaceFromWith_replaces_the_input_endpoint()
    {
        AddRoutes(b => b.From("timer:never?period=3600000&delay=3600000").RouteId("r").To("mock:result"));

        await AdviceWith.RouteAsync(Context, null, a => a.ReplaceFromWith("direct:start"));
        await Context.StartAsync();
        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal("direct:start", Context.GetRoute("r")!.Endpoint.EndpointUri);
        Assert.Equal(["x"], Mock("result").ReceivedBodies);
    }

    [Fact]
    public async Task Advising_a_started_route_restarts_it_with_the_advice()
    {
        AddOrderRoute();
        await Context.StartAsync();
        await Template.SendBodyAsync("direct:start", "before");

        await AdviceWith.RouteAsync(Context, "orders", a => a.WeaveById("b").Replace(r => r.To("mock:advised")));
        await Template.SendBodyAsync("direct:start", "after");

        Assert.Equal(ServiceStatus.Started, Context.GetRouteStatus("orders"));
        Assert.Equal(["before>set"], Mock("b").ReceivedBodies);
        Assert.Equal(["after>set"], Mock("advised").ReceivedBodies);
        Assert.Equal(2, Mock("a").ReceivedCounter);
    }

    [Fact]
    public async Task Generated_ids_are_assigned_before_advising_and_stay_stable()
    {
        AddRoutes(b => b.From("direct:start").RouteId("r").To("mock:a").To("mock:b"));
        await Context.StartAsync();
        var ids = Context.RouteDefinitions.Single().Outputs.Select(o => o.Id).ToList();
        Assert.All(ids, Assert.NotNull);

        await AdviceWith.RouteAsync(Context, "r", a => a.WeaveById(ids[1]!).Remove());
        await AdviceWith.RouteAsync(Context, "r", a => a.WeaveById(ids[0]!).After(r => r.To("mock:c")));
        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(ids[0], Context.RouteDefinitions.Single().Outputs[0].Id);
        Assert.Equal(1, Mock("a").ReceivedCounter);
        Assert.Equal(0, Mock("b").ReceivedCounter);
        Assert.Equal(1, Mock("c").ReceivedCounter);
    }

    [Fact]
    public async Task A_weave_matching_nothing_fails_and_leaves_a_started_route_running()
    {
        AddOrderRoute();
        await Context.StartAsync();

        var e = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AdviceWith.RouteAsync(Context, "orders", a => a.WeaveById("missing").Remove()));
        await Template.SendBodyAsync("direct:start", "x");

        Assert.Contains("There are no outputs which matches: id: missing in the route: orders", e.Message);
        Assert.Equal(1, Mock("result").ReceivedCounter);
    }

    [Fact]
    public async Task Unknown_route_or_ambiguous_null_route_id_fails()
    {
        AddRoutes(b =>
        {
            b.From("direct:a").To("mock:a");
            b.From("direct:b").To("mock:b");
        });

        await Assert.ThrowsAsync<ArgumentException>(() => AdviceWith.RouteAsync(Context, "nope", _ => { }));
        await Assert.ThrowsAsync<ArgumentException>(() => AdviceWith.RouteAsync(Context, null, _ => { }));
    }

    [Fact]
    public async Task MockEndpoints_sends_to_mock_endpoints_too()
    {
        AddRoutes(b =>
        {
            b.From("direct:start").RouteId("start").To("direct:foo").To("log:foo");
            b.From("direct:foo").To("mock:foo-result");
        });

        await AdviceWith.RouteAsync(Context, "start", a => a.MockEndpoints());
        await Context.StartAsync();
        Mock("mock:direct:start").ExpectedBodiesReceived("x");
        Mock("mock:direct:foo").ExpectedBodiesReceived("x");
        Mock("mock:log:foo").ExpectedBodiesReceived("x");

        await Template.SendBodyAsync("direct:start", "x");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        Assert.Equal(1, Mock("foo-result").ReceivedCounter);
        Assert.DoesNotContain(Context.Endpoints, e => e.EndpointUri.StartsWith("mock:mock:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MockEndpoints_with_patterns_only_mocks_matching_endpoints()
    {
        AddRoutes(b =>
        {
            b.From("direct:start").RouteId("start").To("direct:foo").To("log:foo?level=DEBUG");
            b.From("direct:foo").To("mock:foo-result");
        });

        await AdviceWith.RouteAsync(Context, "start", a => a.MockEndpoints("log:*"));
        await Context.StartAsync();
        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(1, Mock("mock:log:foo").ReceivedCounter);
        Assert.DoesNotContain(Context.Endpoints, e => e.EndpointUri == "mock:direct:foo");
    }

    [Fact]
    public async Task MockEndpointsAndSkip_does_not_send_to_the_original_endpoint()
    {
        AddRoutes(b =>
        {
            b.From("direct:start").RouteId("start").To("direct:remote").To("mock:result");
            b.From("direct:remote").To("mock:remote-called");
        });

        await AdviceWith.RouteAsync(Context, "start", a => a.MockEndpointsAndSkip("direct:remote"));
        await Context.StartAsync();
        Mock("mock:direct:remote").WhenAnyExchangeReceived(e => e.Message.Body = "stubbed");
        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(1, Mock("mock:direct:remote").ReceivedCounter);
        Assert.Equal(0, Mock("remote-called").ReceivedCounter);
        Assert.Equal(["stubbed"], Mock("result").ReceivedBodies);
    }

    [Fact]
    public async Task MockEndpoints_on_a_started_context_applies_at_once()
    {
        AddRoutes(b =>
        {
            b.From("direct:start").RouteId("start").To("direct:foo");
            b.From("direct:foo").To("mock:foo-result");
        });
        await Context.StartAsync();

        await AdviceWith.RouteAsync(Context, "start", a => a.MockEndpointsAndSkip("direct:foo"));
        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(1, Mock("mock:direct:foo").ReceivedCounter);
        Assert.Equal(0, Mock("foo-result").ReceivedCounter);
    }

    [Fact]
    public async Task Advice_can_add_intercepts_to_the_route()
    {
        AddRoutes(b =>
        {
            b.From("direct:start").RouteId("start").To("direct:payments").To("mock:result");
            b.From("direct:payments").To("mock:payments-called");
            b.From("direct:other").RouteId("other").To("mock:other");
        });

        await AdviceWith.RouteAsync(Context, "start", a =>
        {
            a.InterceptSendToEndpoint("direct:payments", i => i.To("mock:payments"), skipSendToOriginalEndpoint: true);
            a.Intercept(i => i.To("mock:step"));
            a.InterceptFrom(null, i => i.To("mock:entered"));
        });
        await Context.StartAsync();
        await Template.SendBodyAsync("direct:start", "x");
        await Template.SendBodyAsync("direct:other", "y");

        Assert.Equal(1, Mock("payments").ReceivedCounter);
        Assert.Equal(0, Mock("payments-called").ReceivedCounter);
        Assert.Equal(2, Mock("step").ReceivedCounter);
        Assert.Equal(["x"], Mock("entered").ReceivedBodies);
        Assert.Equal(1, Mock("other").ReceivedCounter);
    }

    [Fact]
    public async Task Advice_composes_with_error_handler_onException_and_onCompletion()
    {
        AddRoutes(b =>
        {
            b.OnException<InvalidOperationException>(o => o.Handled(true).To("mock:error"));
            b.From("direct:start").RouteId("r")
                .OnCompletion(c => c.To("mock:done"))
                .To("mock:remote").Id("remote")
                .To("mock:result");
        });
        await Context.StartAsync();

        await AdviceWith.RouteAsync(Context, "r", a =>
            a.WeaveById("remote").Replace(r => r.Process(_ => throw new InvalidOperationException("remote down"))));
        Mock("done").ExpectedMessageCount(1);
        await Template.SendBodyAsync("direct:start", "x");

        await Mock("done").AssertIsSatisfiedAsync();
        Assert.Equal(1, Mock("error").ReceivedCounter);
        Assert.Equal(0, Mock("result").ReceivedCounter);
        Assert.Equal(0, Mock("remote").ReceivedCounter);
    }

    [Fact]
    public async Task Replace_with_nothing_removes_and_invalid_replacements_fail()
    {
        AddRoutes(b => b.From("direct:start").RouteId("r")
            .Choice(c => c.When(_ => true, r => r.To("mock:when")).Otherwise(r => r.To("mock:otherwise")))
            .To("mock:a"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AdviceWith.RouteAsync(Context, "r", a => a.WeaveByType<WhenDefinition>().Before(r => r.To("mock:x"))));
        await AdviceWith.RouteAsync(Context, "r", a => a.WeaveByToUri("mock:a").Replace(_ => { }));
        await Context.StartAsync();
        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(0, Mock("a").ReceivedCounter);
        Assert.Equal(1, Mock("when").ReceivedCounter);
    }
}
