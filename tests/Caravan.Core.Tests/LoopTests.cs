using Caravan.Builder;
using Caravan.Components.Mock;

namespace Caravan.Core.Tests;

/// <summary>loop semantics (mirrors Camel's LoopTest, LoopCopyTest, LoopDoWhileTest).</summary>
public class LoopTests : ContextTestBase
{
    [Fact]
    public async Task Loop_runs_the_block_count_times_with_index_and_size()
    {
        await StartWithRoutes(b => b.From("direct:start").Loop(3, l => l.To("mock:loop")).To("mock:result"));
        Mock("loop").ExpectedMessageCount(3);
        Mock("result").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "x");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        Assert.Equal([0, 1, 2], Mock("loop").ReceivedExchanges.Select(e => e.GetProperty(ExchangePropertyKeys.LoopIndex)));
        Assert.All(Mock("loop").ReceivedExchanges, e => Assert.Equal(3, e.GetProperty(ExchangePropertyKeys.LoopSize)));
        Assert.Equal(2, Mock("result").ReceivedExchanges[0].GetProperty(ExchangePropertyKeys.LoopIndex));
    }

    [Fact]
    public async Task Loop_count_from_an_expression_converted_to_int()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:header").Loop(Expr.Header("loop"), l => l.To("mock:loop"));
            b.From("direct:func").Loop(e => e.Message.Body, l => l.To("mock:loop"));
        });
        Mock("loop").ExpectedMessageCount(5);

        await Template.SendBodyAndHeaderAsync("direct:header", "x", "loop", "2");
        await Template.SendBodyAsync("direct:func", 3);

        await Mock("loop").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Loop_of_zero_or_null_skips_the_block()
    {
        await StartWithRoutes(b => b.From("direct:start").Loop(Expr.Header("loop"), l => l.To("mock:loop")).To("mock:result"));
        ExpectNone("loop");
        Mock("result").ExpectedMessageCount(2);

        await Template.SendBodyAndHeaderAsync("direct:start", "x", "loop", 0);
        await Template.SendBodyAsync("direct:start", "y");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Without_copy_each_iteration_processes_the_previous_output()
    {
        await StartWithRoutes(b => b.From("direct:start").Loop(3, l => l.Transform(e => e.Message.Body + "B").To("mock:loop")).To("mock:result"));
        Mock("loop").ExpectedBodiesReceived("AB", "ABB", "ABBB");
        Mock("result").ExpectedBodiesReceived("ABBB");

        await Template.SendBodyAsync("direct:start", "A");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task With_copy_each_iteration_starts_from_the_input_and_the_last_result_wins()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Loop(3, l => l.Transform(e => e.Message.Body + "B").SetHeader("i", Expr.Property(ExchangePropertyKeys.LoopIndex)).To("mock:loop"), copy: true)
            .To("mock:result"));
        Mock("loop").ExpectedBodiesReceived("AB", "AB", "AB");
        Mock("result").ExpectedBodiesReceived("AB");
        Mock("result").ExpectedHeaderReceived("i", 2);

        await Template.SendBodyAsync("direct:start", "A");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Loop_while_checks_the_predicate_on_each_output()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .LoopWhile((string s) => s.Length <= 4, l => l.Transform(e => e.Message.Body + "A").To("mock:loop"))
            .To("mock:result"));
        Mock("loop").ExpectedBodiesReceived("AA", "AAA", "AAAA", "AAAAA");
        Mock("result").ExpectedBodiesReceived("AAAAA");

        await Template.SendBodyAsync("direct:start", "A");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        Assert.Null(Mock("result").ReceivedExchanges[0].GetProperty(ExchangePropertyKeys.LoopSize));
    }

    [Fact]
    public async Task Loop_while_false_at_start_skips_the_block()
    {
        await StartWithRoutes(b => b.From("direct:start").LoopWhile(_ => false, l => l.To("mock:loop")).To("mock:result"));
        ExpectNone("loop");
        Mock("result").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "x");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Loop_while_with_copy_evaluates_the_predicate_on_the_last_result()
    {
        var iterations = 0;
        await StartWithRoutes(b => b.From("direct:start")
            .LoopWhile(e => e.Message.GetHeader("n", 0) < 3, l => l.Process(e => e.Message.SetHeader("n", ++iterations)), copy: true)
            .To("mock:result"));
        Mock("result").ExpectedHeaderReceived("n", 3);

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("result").AssertIsSatisfiedAsync();
        Assert.Equal(3, iterations);
    }

    [Fact]
    public async Task Failure_ends_the_loop_and_fails_the_exchange()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Loop(5, l => l.To("mock:loop").Filter(e => e.GetProperty<int>(ExchangePropertyKeys.LoopIndex) == 1, f => f.ThrowException(new InvalidOperationException("boom"))))
            .To("mock:result"));
        Mock("loop").ExpectedMessageCount(2);
        ExpectNone("result");

        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", "x"));

        Assert.IsType<InvalidOperationException>(ex.InnerException);
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Stop_in_the_loop_stops_the_exchange()
    {
        await StartWithRoutes(b => b.From("direct:start").Loop(5, l => l.To("mock:loop").Stop(), copy: true).To("mock:result"));
        Mock("loop").ExpectedMessageCount(1);
        ExpectNone("result");

        await Template.SendBodyAsync("direct:start", "x");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Nested_loops_have_their_own_index()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Loop(2, outer => outer
                .SetHeader("outer", Expr.Property(ExchangePropertyKeys.LoopIndex))
                .Loop(2, inner => inner.To("mock:inner"))));
        Mock("inner").ExpectedMessageCount(4);

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("inner").AssertIsSatisfiedAsync();
        Assert.Equal([0, 0, 1, 1], Mock("inner").ReceivedExchanges.Select(e => e.Message.GetHeader("outer")));
        Assert.Equal([0, 1, 0, 1], Mock("inner").ReceivedExchanges.Select(e => e.GetProperty(ExchangePropertyKeys.LoopIndex)));
    }

    [Fact]
    public async Task Cancellation_fails_a_loop_unless_break_on_shutdown()
    {
        using var cancelA = new CancellationTokenSource();
        using var cancelB = new CancellationTokenSource();
        await StartWithRoutes(b =>
        {
            b.From("direct:fail").LoopWhile(_ => true, l => l.Process(e =>
            {
                if (e.GetProperty<int>(ExchangePropertyKeys.LoopIndex) == 10)
                {
                    cancelA.Cancel();
                }
            })).To("mock:fail");
            b.From("direct:break").LoopWhile(_ => true, l => l.Process(e =>
            {
                if (e.GetProperty<int>(ExchangePropertyKeys.LoopIndex) == 10)
                {
                    cancelB.Cancel();
                }
            }), breakOnShutdown: true).To("mock:break");
        });
        ExpectNone("fail");
        Mock("break").ExpectedMessageCount(1);

        var failed = await Template.SendAsync("direct:fail", new Exchange(Context) { CancellationToken = cancelA.Token });
        var broken = await Template.SendAsync("direct:break", new Exchange(Context) { CancellationToken = cancelB.Token });

        Assert.IsAssignableFrom<OperationCanceledException>(failed.Exception);
        Assert.Null(broken.Exception);
        Assert.Equal(10, broken.GetProperty<int>(ExchangePropertyKeys.LoopIndex));
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }
}
