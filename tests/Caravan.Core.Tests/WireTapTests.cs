using Caravan.Builder;
using Caravan.Components.Mock;
using Caravan.Impl;
using Caravan.Processors;

namespace Caravan.Core.Tests;

public class WireTapTests : ContextTestBase
{
    [Fact]
    public async Task Tap_receives_a_copy_and_changes_do_not_affect_the_original()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start").SetHeader("h", Expr.Constant("orig")).WireTap("direct:tap").To("mock:result");
            b.From("direct:tap").SetBody(_ => "changed by tap").SetHeader("h", Expr.Constant("tap")).To("mock:tap");
        });
        var result = Mock("result");
        result.ExpectedBodiesReceived("hello");
        result.ExpectedHeaderReceived("h", "orig");
        var tap = Mock("tap");
        tap.ExpectedBodiesReceived("changed by tap");

        var sent = await Template.SendAsync("direct:start", e => e.Message.Body = "hello");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        var tapped = tap.ReceivedExchanges[0];
        Assert.NotEqual(sent.ExchangeId, tapped.ExchangeId);
        Assert.Equal(sent.ExchangeId, tapped.GetProperty(ExchangePropertyKeys.CorrelationId));
        Assert.Equal(ExchangePattern.InOnly, tapped.Pattern);
        Assert.Equal("hello", sent.Message.Body);
    }

    [Fact]
    public async Task Original_continues_without_waiting_for_the_tap()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await StartWithRoutes(b =>
        {
            b.From("direct:start").WireTap("direct:slow").To("mock:result");
            b.From("direct:slow").Process(async _ => await release.Task).To("mock:tap");
        });
        Mock("result").ExpectedMessageCount(1);
        Mock("tap").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("result").AssertIsSatisfiedAsync();
        Assert.Equal(0, Mock("tap").ReceivedCounter);
        release.SetResult();
        await Mock("tap").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Copy_false_sends_a_new_empty_exchange()
    {
        await StartWithRoutes(b => b.From("direct:start").WireTap("mock:tap", copy: false));
        var tap = Mock("tap");
        tap.ExpectedMessageCount(1);

        await Template.SendBodyAndHeaderAsync("direct:start", "body", "h", 1);

        await tap.AssertIsSatisfiedAsync();
        Assert.Null(tap.ReceivedBodies[0]);
        Assert.Empty(tap.ReceivedExchanges[0].Message.Headers);
    }

    [Fact]
    public async Task On_prepare_modifies_only_the_tapped_copy_and_dynamic_uri_is_evaluated_after_it()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .WireTap("mock:${header.tapTo}", onPrepare: e =>
            {
                e.Message.Body = "prepared";
                e.Message.SetHeader("tapTo", "prepared-tap");
            })
            .To("mock:result"));
        Mock("prepared-tap").ExpectedBodiesReceived("prepared");
        Mock("result").ExpectedBodiesReceived("original");
        Mock("result").ExpectedHeaderReceived("tapTo", "ignored");

        await Template.SendBodyAndHeaderAsync("direct:start", "original", "tapTo", "ignored");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Tap_failure_does_not_affect_the_original()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start").WireTap("direct:failing").To("mock:result");
            b.From("direct:failing").To("mock:tap").Process(_ => throw new InvalidOperationException("tap failed"));
        });
        Mock("result").ExpectedMessageCount(1);
        Mock("tap").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "x");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Stopping_the_route_waits_for_outstanding_taps()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = false;
        await StartWithRoutes(b => b.From("direct:start").RouteId("main").WireTap("mock:tap"));
        Mock("tap").WhenAnyExchangeReceived(new Caravan.Spi.DelegateProcessor(async _ =>
        {
            entered.SetResult();
            await release.Task;
            finished = true;
        }));

        await Template.SendBodyAsync("direct:start", "x");
        await entered.Task;
        var processor = ((Route)Context.GetRoute("main")!).RouteContext.Services.OfType<WireTapProcessor>().Single();
        Assert.Equal(1, processor.PendingCount);

        var stop = Context.StopRouteAsync("main");
        Assert.False(stop.IsCompleted);
        release.SetResult();
        await stop;

        Assert.True(finished);
        Assert.Equal(0, processor.PendingCount);
    }
}
