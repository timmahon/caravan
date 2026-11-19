using Caravan.Builder;
using Caravan.Components.Mock;
using Microsoft.Extensions.Logging;

namespace Caravan.Core.Tests;

/// <summary>onCompletion semantics (mirrors Camel's OnCompletion* tests).</summary>
public class OnCompletionTests : ContextTestBase
{
    [Fact]
    public async Task Runs_after_the_route_completed_on_a_copy_that_cannot_change_the_reply()
    {
        var order = new List<string>();
        await StartWithRoutes(b => b.From("direct:start")
            .OnCompletion(c => c.Process(_ => order.Add("completion")).To("mock:done").SetBody(_ => "changed by onCompletion"))
            .Process(_ => order.Add("route"))
            .SetBody(e => "Bye " + e.Message.Body)
            .To("mock:result"));

        var reply = await Template.RequestBodyAsync<string>("direct:start", "World");

        Assert.Equal("Bye World", reply);
        Assert.Equal(["route", "completion"], order);
        var done = Assert.Single(Mock("done").ReceivedExchanges);
        Assert.Equal("Bye World", done.Message.Body);
        Assert.True(done.GetProperty<bool>(ExchangePropertyKeys.OnCompletion));
        Assert.Equal(ExchangePattern.InOnly, done.Pattern);
        Assert.NotNull(done.GetProperty(ExchangePropertyKeys.CorrelationId));
    }

    [Fact]
    public async Task On_complete_only_and_on_failure_only()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .OnCompletion(c => c.OnCompleteOnly().To("mock:complete"))
            .OnCompletion(c => c.OnFailureOnly().To("mock:failure"))
            .Filter(Expr.Body().IsEqualTo("fail"), f => f.ThrowException(new InvalidOperationException("boom"))));

        await Template.SendBodyAsync("direct:start", "ok");
        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", "fail"));

        Assert.Equal(["ok"], Mock("complete").ReceivedBodies);
        var failure = Assert.Single(Mock("failure").ReceivedExchanges);
        Assert.Equal("fail", failure.Message.Body);
        Assert.Null(failure.Exception);
        Assert.Equal("boom", failure.GetProperty<Exception>(ExchangePropertyKeys.ExceptionCaught)!.Message);
    }

    [Fact]
    public async Task On_when_filters_the_completion()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .OnCompletion(c => c.OnWhen(Expr.Body().Contains("Hello")).To("mock:done"))
            .To("mock:result"));

        await Template.SendBodyAsync("direct:start", "Hello World");
        await Template.SendBodyAsync("direct:start", "Bye World");

        Assert.Equal(["Hello World"], Mock("done").ReceivedBodies);
    }

    [Fact]
    public async Task Before_consumer_mode_can_change_the_reply()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .OnCompletion(c => c.ModeBeforeConsumer().SetHeader("completedBy", Expr.Constant("onCompletion")).SetBody(e => e.Message.Body + "!"))
            .SetBody(e => "Hello " + e.Message.Body));

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "World", ExchangePattern.InOut);

        Assert.Equal("Hello World!", exchange.Message.Body);
        Assert.Equal("onCompletion", exchange.Message.GetHeader("completedBy"));
        Assert.False(exchange.Properties.ContainsKey(ExchangePropertyKeys.OnCompletion));
    }

    [Fact]
    public async Task Before_consumer_runs_before_after_consumer_and_keeps_the_failure()
    {
        var order = new List<string>();
        await StartWithRoutes(b => b.From("direct:start")
            .OnCompletion(c => c.Process(_ => order.Add("after")))
            .OnCompletion(c => c.ModeBeforeConsumer().OnFailureOnly().Process(_ => order.Add("before")))
            .ThrowException(new InvalidOperationException("boom")));

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.Equal("boom", exchange.Exception!.Message);
        Assert.Equal(["before", "after"], order);
    }

    [Fact]
    public async Task Global_on_completion_applies_to_routes_without_their_own()
    {
        await StartWithRoutes(b =>
        {
            b.OnCompletion(c => c.To("mock:global"));
            b.From("direct:a").To("mock:a");
            b.From("direct:b").OnCompletion(c => c.To("mock:route")).To("mock:b");
        });

        await Template.SendBodyAsync("direct:a", "a");
        await Template.SendBodyAsync("direct:b", "b");

        Assert.Equal(["a"], Mock("global").ReceivedBodies);
        Assert.Equal(["b"], Mock("route").ReceivedBodies);
    }

    [Fact]
    public async Task Runs_once_per_exchange_across_sub_routes()
    {
        await StartWithRoutes(b =>
        {
            b.OnCompletion(c => c.To("mock:global"));
            b.From("direct:start").To("direct:sub").SetBody(_ => "end");
            b.From("direct:sub").To("mock:sub");
        });

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(["end"], Mock("global").ReceivedBodies);
    }

    [Fact]
    public async Task Sub_route_on_completion_runs_when_the_whole_exchange_completes()
    {
        var order = new List<string>();
        await StartWithRoutes(b =>
        {
            b.From("direct:start").To("direct:sub").Process(_ => order.Add("caller after sub"));
            b.From("direct:sub").OnCompletion(c => c.Process(_ => order.Add("sub completion"))).Process(_ => order.Add("sub"));
        });

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(["sub", "caller after sub", "sub completion"], order);
    }

    [Fact]
    public async Task Parallel_processing_runs_in_the_background()
    {
        var release = TestHelpers.NewSignal();
        await StartWithRoutes(b => b.From("direct:start")
            .OnCompletion(c => c.ParallelProcessing().Process(async _ => await release.Task).To("mock:done"))
            .To("mock:result"));
        Mock("done").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "x").Bounded();
        Assert.Equal(0, Mock("done").ReceivedCounter);
        release.SetResult();

        await Mock("done").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Handled_failure_counts_as_completed()
    {
        await StartWithRoutes(b =>
        {
            b.OnException<IOException>(o => o.Handled(true));
            b.From("direct:start")
                .OnCompletion(c => c.OnCompleteOnly().To("mock:complete"))
                .OnCompletion(c => c.OnFailureOnly().To("mock:failure"))
                .ThrowException(new IOException());
        });

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(1, Mock("complete").ReceivedCounter);
        Assert.Equal(0, Mock("failure").ReceivedCounter);
    }

    [Fact]
    public async Task Use_original_message()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .OnCompletion(c => c.UseOriginalMessage().To("mock:done"))
            .SetBody(_ => "changed"));

        await Template.SendBodyAsync("direct:start", "original");

        Assert.Equal(["original"], Mock("done").ReceivedBodies);
    }

    [Fact]
    public async Task Failure_in_the_block_is_logged_and_does_not_change_the_outcome()
    {
        var logs = new CapturingLoggerProvider();
        Context.LoggerFactory = LoggerFactory.Create(b => b.AddProvider(logs));
        await StartWithRoutes(b => b.From("direct:start")
            .OnCompletion(c => c.ModeBeforeConsumer().ThrowException(new InvalidOperationException("before failed")))
            .OnCompletion(c => c.ThrowException(new InvalidOperationException("after failed")))
            .To("mock:result"));

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.False(exchange.IsFailed);
        Assert.Equal(2, logs.Messages.Count(m => m.Level == LogLevel.Warning && m.Category.EndsWith("OnCompletionProcessor", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Is_handed_over_with_the_exchange_to_seda()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start").OnCompletion(c => c.To("mock:done")).To("seda:next");
            b.From("seda:next").SetBody(_ => "processed by seda").To("mock:seda");
        });
        Mock("done").ExpectedBodiesReceived("processed by seda");

        await Template.SendBodyAsync("direct:start", "x");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public void Complete_only_and_failure_only_cannot_be_combined()
    {
        var definition = new Model.OnCompletionDefinition { OnCompleteOnly = true, OnFailureOnly = true };

        Assert.Throws<InvalidOperationException>(() => definition.CreateProcessor(new Spi.RouteContext(Context, new Model.RouteDefinition("direct:x"), "r")));
    }
}
