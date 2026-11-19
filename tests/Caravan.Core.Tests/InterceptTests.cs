using Caravan.Builder;
using Caravan.Components.Mock;

namespace Caravan.Core.Tests;

/// <summary>intercept, interceptFrom and interceptSendToEndpoint (mirrors Camel's Intercept*Test).</summary>
public class InterceptTests : ContextTestBase
{
    // ----- intercept -----

    [Fact]
    public async Task Intercept_runs_before_every_node_with_the_node_ids()
    {
        await StartWithRoutes(b =>
        {
            b.Intercept(i => i.To("mock:intercepted"));
            b.From("direct:start").RouteId("start").SetBody(_ => "x").Id("set").To("mock:a").Id("toA").To("mock:b").Id("toB");
        });
        Mock("a").ExpectedBodiesReceived("x");
        Mock("b").ExpectedBodiesReceived("x");

        await Template.SendBodyAsync("direct:start", "in");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        var intercepted = Mock("intercepted").ReceivedExchanges;
        Assert.Equal(["set", "toA", "toB"], intercepted.Select(e => e.GetProperty<string>(ExchangePropertyKeys.InterceptedNodeId)));
        Assert.All(intercepted, e => Assert.Equal("start", e.GetProperty<string>(ExchangePropertyKeys.InterceptedRouteId)));
        Assert.All(intercepted, e => Assert.Equal("direct:start", e.GetProperty<string>(ExchangePropertyKeys.InterceptedRouteEndpointUri)));
        Assert.Equal(["in", "x", "x"], intercepted.Select(e => e.Message.Body));
    }

    [Fact]
    public async Task Intercept_with_when_only_runs_for_matching_exchanges()
    {
        await StartWithRoutes(b =>
        {
            b.Intercept(i => i.When(e => e.Message.GetBody<string>() == "Hello").To("mock:intercepted"));
            b.From("direct:start").To("mock:a").To("mock:b");
        });

        await Template.SendBodyAsync("direct:start", "Hello");
        await Template.SendBodyAsync("direct:start", "Bye");

        Assert.Equal(2, Mock("intercepted").ReceivedCounter);
        Assert.Equal(["Hello", "Bye"], Mock("b").ReceivedBodies);
    }

    [Fact]
    public async Task Stop_in_intercept_stops_the_exchange()
    {
        await StartWithRoutes(b =>
        {
            b.Intercept(i => i.When(e => e.Message.GetBody<string>() == "stop").To("mock:stopped").Stop());
            b.From("direct:start").To("mock:a").To("mock:b");
        });

        var reply = await Template.RequestBodyAsync<string>("direct:start", "stop");
        await Template.SendBodyAsync("direct:start", "go");

        Assert.Equal("stop", reply);
        Assert.Equal(1, Mock("stopped").ReceivedCounter);
        Assert.Equal(["go"], Mock("a").ReceivedBodies);
        Assert.Equal(["go"], Mock("b").ReceivedBodies);
    }

    [Fact]
    public async Task Intercept_applies_to_nested_nodes()
    {
        await StartWithRoutes(b =>
        {
            b.Intercept(i => i.To("mock:intercepted"));
            b.From("direct:start")
                .Choice(c => c.When(_ => true, r => r.To("mock:a").To("mock:b")))
                .Split(Expr.Constant(new[] { 1, 2 }), s => s.To("mock:part"));
        });

        await Template.SendBodyAsync("direct:start", "x");

        // choice, to a, to b, split, then "to part" once per part.
        Assert.Equal(6, Mock("intercepted").ReceivedCounter);
        Assert.Equal(2, Mock("part").ReceivedCounter);
    }

    [Fact]
    public async Task Intercept_applies_to_the_routes_of_its_builder_only()
    {
        Context.AddRoutes(RouteBuilder.Create(b =>
        {
            b.Intercept(i => i.To("mock:intercepted"));
            b.From("direct:a").To("mock:a");
            b.From("direct:b").To("mock:b");
        }));
        Context.AddRoutes(RouteBuilder.Create(b => b.From("direct:c").To("mock:c")));
        await Context.StartAsync();

        await Template.SendBodyAsync("direct:a", "a");
        await Template.SendBodyAsync("direct:b", "b");
        await Template.SendBodyAsync("direct:c", "c");

        Assert.Equal(["a", "b"], Mock("intercepted").ReceivedBodies);
    }

    [Fact]
    public async Task Intercept_does_not_apply_to_onException_and_onCompletion_blocks()
    {
        await StartWithRoutes(b =>
        {
            b.Intercept(i => i.To("mock:intercepted"));
            b.OnException<InvalidOperationException>(o => o.Handled(true).To("mock:error"));
            b.From("direct:start")
                .OnCompletion(c => c.To("mock:done"))
                .ThrowException(new InvalidOperationException("boom"));
        });
        Mock("done").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("done").AssertIsSatisfiedAsync();
        Assert.Equal(1, Mock("error").ReceivedCounter);
        Assert.Equal(1, Mock("intercepted").ReceivedCounter);
    }

    [Fact]
    public async Task Failure_in_the_intercept_block_is_handled_by_the_route_error_handler()
    {
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead"));
            b.Intercept(i => i.Process(_ => throw new InvalidOperationException("intercept failed")));
            b.From("direct:start").To("mock:a");
        });

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(0, Mock("a").ReceivedCounter);
        var dead = Assert.Single(Mock("dead").ReceivedExchanges);
        Assert.Equal("intercept failed", dead.GetProperty<Exception>(ExchangePropertyKeys.ExceptionCaught)!.Message);
    }

    [Fact]
    public async Task Stopped_exchange_still_runs_onCompletion()
    {
        await StartWithRoutes(b =>
        {
            b.Intercept(i => i.Stop());
            b.From("direct:start").OnCompletion(c => c.OnCompleteOnly().To("mock:done")).To("mock:a");
        });
        Mock("done").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("done").AssertIsSatisfiedAsync();
        Assert.Equal(0, Mock("a").ReceivedCounter);
    }

    // ----- interceptFrom -----

    [Fact]
    public async Task InterceptFrom_runs_when_an_exchange_enters_each_route()
    {
        await StartWithRoutes(b =>
        {
            b.InterceptFrom(i => i.To("mock:intercepted"));
            b.From("direct:a").To("mock:a").To("direct:b");
            b.From("direct:b").To("mock:b");
        });

        await Template.SendBodyAsync("direct:a", "x");

        var intercepted = Mock("intercepted").ReceivedExchanges;
        Assert.Equal(["direct:a", "direct:b"], intercepted.Select(e => e.Message.GetHeader<string>(CaravanHeaders.InterceptedEndpoint)));
        Assert.Equal("direct:a", intercepted[0].GetProperty<string>(ExchangePropertyKeys.InterceptedEndpoint));
        Assert.Equal(1, Mock("b").ReceivedCounter);
    }

    [Theory]
    [InlineData("direct:a*")]
    [InlineData("direct://a1")]
    [InlineData("(direct|seda):a.*")]
    public async Task InterceptFrom_only_matches_routes_whose_endpoint_matches_the_pattern(string pattern)
    {
        await StartWithRoutes(b =>
        {
            b.InterceptFrom(pattern, i => i.To("mock:intercepted"));
            b.From("direct:a1").To("mock:a");
            b.From("direct:b").To("mock:b");
        });

        await Template.SendBodyAsync("direct:a1", "a");
        await Template.SendBodyAsync("direct:b", "b");

        Assert.Equal(["a"], Mock("intercepted").ReceivedBodies);
        Assert.Equal(1, Mock("b").ReceivedCounter);
    }

    [Fact]
    public async Task InterceptFrom_with_when_and_stop_filters_messages()
    {
        await StartWithRoutes(b =>
        {
            b.InterceptFrom("direct:*", i => i.When(e => e.Message.GetBody<string>()!.StartsWith("bad", StringComparison.Ordinal)).To("mock:rejected").Stop());
            b.From("direct:start").To("mock:result");
        });

        await Template.SendBodyAsync("direct:start", "bad order");
        await Template.SendBodyAsync("direct:start", "good order");

        Assert.Equal(["bad order"], Mock("rejected").ReceivedBodies);
        Assert.Equal(["good order"], Mock("result").ReceivedBodies);
    }

    // ----- interceptSendToEndpoint -----

    [Fact]
    public async Task InterceptSendToEndpoint_runs_the_block_then_sends_to_the_original_endpoint()
    {
        await StartWithRoutes(b =>
        {
            b.InterceptSendToEndpoint("mock:foo", i => i.To("mock:detour").Transform(e => e.Message.GetBody<string>() + " detoured"));
            b.From("direct:start").To("mock:foo").To("mock:result");
        });

        await Template.SendBodyAsync("direct:start", "Hello");

        var detour = Assert.Single(Mock("detour").ReceivedExchanges);
        Assert.Equal("mock:foo", detour.Message.GetHeader<string>(CaravanHeaders.InterceptedEndpoint));
        Assert.Equal(["Hello detoured"], Mock("foo").ReceivedBodies);
        Assert.Equal(["Hello detoured"], Mock("result").ReceivedBodies);
    }

    [Fact]
    public async Task InterceptSendToEndpoint_can_skip_the_original_endpoint()
    {
        await StartWithRoutes(b =>
        {
            b.InterceptSendToEndpoint("mock:foo", i => i.To("mock:detour"), skipSendToOriginalEndpoint: true);
            b.From("direct:start").To("mock:foo").To("mock:result");
        });

        await Template.SendBodyAsync("direct:start", "Hello");

        Assert.Equal(1, Mock("detour").ReceivedCounter);
        Assert.Equal(0, Mock("foo").ReceivedCounter);
        Assert.Equal(1, Mock("result").ReceivedCounter);
    }

    [Fact]
    public async Task InterceptSendToEndpoint_with_when_only_detours_and_skips_matching_exchanges()
    {
        await StartWithRoutes(b =>
        {
            b.InterceptSendToEndpoint("mock:foo", i => i.When(e => e.Message.GetBody<string>() == "Hi").SkipSendToOriginalEndpoint().To("mock:detour"));
            b.From("direct:start").To("mock:foo");
        });

        await Template.SendBodyAsync("direct:start", "Hi");
        await Template.SendBodyAsync("direct:start", "Bye");

        Assert.Equal(["Hi"], Mock("detour").ReceivedBodies);
        Assert.Equal(["Bye"], Mock("foo").ReceivedBodies);
    }

    [Fact]
    public async Task InterceptSendToEndpoint_sends_to_the_after_uri_with_the_reply_of_the_original_endpoint()
    {
        await StartWithRoutes(b =>
        {
            b.InterceptSendToEndpoint("mock:foo", i => i.To("mock:before").AfterUri("mock:after"));
            b.From("direct:start").To("mock:foo").To("mock:result");
        });
        Mock("foo").ReturnReplyBody(_ => "reply from foo");

        await Template.SendBodyAsync("direct:start", "Hello");

        Assert.Equal(["Hello"], Mock("before").ReceivedBodies);
        Assert.Equal(1, Mock("foo").ReceivedCounter);
        Assert.Equal(["reply from foo"], Mock("after").ReceivedBodies);
        Assert.Equal(["reply from foo"], Mock("result").ReceivedBodies);
    }

    [Fact]
    public async Task InterceptSendToEndpoint_applies_to_every_sender()
    {
        await StartWithRoutes(b =>
        {
            b.InterceptSendToEndpoint("mock:target*", i => i.To("mock:intercepted"));
            b.From("direct:to").To("mock:target1");
            b.From("direct:toD").ToD("mock:${header.target}");
            b.From("direct:recipientList").RecipientList(Expr.Constant("mock:target3,mock:other"));
            b.From("direct:wireTap").WireTap("mock:target4");
        });
        Mock("target4").ExpectedMessageCount(1);
        Mock("intercepted").ExpectedMessageCount(5);

        await Template.SendBodyAsync("direct:to", "to");
        await Template.SendBodyAndHeaderAsync("direct:toD", "toD", "target", "target2");
        await Template.SendBodyAsync("direct:recipientList", "recipientList");
        await Template.SendBodyAsync("direct:wireTap", "wireTap");
        await Template.SendBodyAsync("mock:target5", "template");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        Assert.Equal(
            ["mock:target1", "mock:target2", "mock:target3", "mock:target4", "mock:target5"],
            Mock("intercepted").ReceivedExchanges.Select(e => e.Message.GetHeader<string>(CaravanHeaders.InterceptedEndpoint)).Order());
        Assert.Equal(1, Mock("other").ReceivedCounter);
    }

    [Fact]
    public async Task InterceptSendToEndpoint_does_not_intercept_the_sends_of_its_own_block()
    {
        await StartWithRoutes(b =>
        {
            b.InterceptSendToEndpoint("mock:*", i => i.To("mock:audit"));
            b.From("direct:start").To("mock:a");
        });

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(1, Mock("a").ReceivedCounter);
        Assert.Equal(1, Mock("audit").ReceivedCounter);
    }

    [Fact]
    public async Task InterceptSendToEndpoint_applies_to_routes_of_other_builders_and_route_to_route_calls()
    {
        Context.AddRoutes(RouteBuilder.Create(b =>
        {
            b.InterceptSendToEndpoint("direct:*", i => i.SetHeader("intercepted", e => e.Message.GetHeader<string>(CaravanHeaders.InterceptedEndpoint)));
            b.From("direct:a").To("direct:b");
        }));
        Context.AddRoutes(RouteBuilder.Create(b =>
        {
            b.From("direct:b").To("mock:b");
            b.From("direct:c").To("direct:b");
        }));
        await Context.StartAsync();

        await Template.SendBodyAsync("direct:c", "x");

        Assert.Equal("direct:b", Mock("b").ReceivedExchanges.Single().Message.GetHeader<string>("intercepted"));
    }

    [Fact]
    public async Task Stop_in_InterceptSendToEndpoint_skips_the_endpoint_and_stops_the_route()
    {
        await StartWithRoutes(b =>
        {
            b.InterceptSendToEndpoint("mock:foo", i => i.To("mock:detour").Stop());
            b.From("direct:start").To("mock:foo").To("mock:result");
        });

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(1, Mock("detour").ReceivedCounter);
        Assert.Equal(0, Mock("foo").ReceivedCounter);
        Assert.Equal(0, Mock("result").ReceivedCounter);
    }

    [Fact]
    public async Task Failure_in_InterceptSendToEndpoint_block_is_handled_by_the_sending_route()
    {
        var attempts = 0;
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead").MaximumRedeliveries(2).RedeliveryDelay(0));
            b.InterceptSendToEndpoint("mock:foo", i => i.Process(_ =>
            {
                attempts++;
                throw new InvalidOperationException("detour failed");
            }));
            b.From("direct:start").To("mock:foo");
        });

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(3, attempts);
        Assert.Equal(0, Mock("foo").ReceivedCounter);
        Assert.Equal(1, Mock("dead").ReceivedCounter);
    }

    [Fact]
    public async Task InterceptSendToEndpoint_survives_a_context_restart()
    {
        await StartWithRoutes(b =>
        {
            b.InterceptSendToEndpoint("mock:foo", i => i.To("mock:detour"), skipSendToOriginalEndpoint: true);
            b.From("direct:start").To("mock:foo");
        });
        await Context.StopAsync();
        await Context.StartAsync();

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(1, Mock("detour").ReceivedCounter);
        Assert.Equal(0, Mock("foo").ReceivedCounter);
    }
}
