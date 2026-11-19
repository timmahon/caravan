using Caravan.Aggregation;
using Caravan.Builder;

namespace Caravan.Core.Tests;

/// <summary>The built-in aggregation strategies (mirrors Camel's *AggregationStrategy tests).</summary>
public class AggregationStrategyTests : ContextTestBase
{
    [Fact]
    public void Use_latest_returns_the_newest_exchange()
    {
        var strategy = AggregationStrategies.UseLatest();
        var a = Create("a");
        var b = Create("b");

        Assert.Same(a, strategy.Aggregate(null, a));
        Assert.Same(b, strategy.Aggregate(a, b));
    }

    [Fact]
    public void Use_latest_propagates_an_earlier_exception_and_keeps_handled_failures()
    {
        var strategy = AggregationStrategies.UseLatest();
        var failed = Create("a");
        failed.Exception = new InvalidOperationException("boom");
        failed.IsRedeliveryExhausted = true;
        failed.SetProperty(ExchangePropertyKeys.FailureEndpoint, "mock:x");
        var next = Create("b");

        var result = strategy.Aggregate(failed, next)!;

        Assert.Same(next, result);
        Assert.Same(failed.Exception, result.Exception);
        Assert.True(result.IsRedeliveryExhausted);
        Assert.Equal("mock:x", result.GetProperty(ExchangePropertyKeys.FailureEndpoint));

        var handled = Create("h");
        handled.ErrorHandlerHandled = true;
        Assert.Same(handled, strategy.Aggregate(handled, Create("c")));
    }

    [Fact]
    public void Use_original_returns_the_bound_original_and_optionally_propagates_exceptions()
    {
        var original = Create("original");
        var failed = Create("a");
        failed.Exception = new InvalidOperationException("boom");

        var propagating = new UseOriginalAggregationStrategy().NewInstance(original);
        Assert.Same(original, propagating.Aggregate(propagating.Aggregate(null, Create("x")), failed));
        Assert.Same(failed.Exception, original.Exception);

        var quiet = Create("quiet");
        var notPropagating = new UseOriginalAggregationStrategy(propagateException: false).NewInstance(quiet);
        Assert.Same(quiet, notPropagating.Aggregate(null, failed));
        Assert.Null(quiet.Exception);
    }

    [Fact]
    public void Grouped_exchange_groups_into_a_new_holder()
    {
        IAggregationStrategy strategy = new GroupedExchangeAggregationStrategy();
        var a = Create("a");
        var b = Create("b");

        var result = strategy.Aggregate(strategy.Aggregate(null, a), b)!;
        strategy.OnCompletion(result, null);

        Assert.NotSame(a, result);
        Assert.Equal([a, b], Assert.IsType<List<Exchange>>(result.Message.Body));
        Assert.Null(result.GetProperty(ExchangePropertyKeys.GroupedExchange));
    }

    [Fact]
    public void Grouped_body_and_grouped_message_collect_bodies_and_messages()
    {
        var bodies = AggregationStrategies.GroupedBody();
        var messages = AggregationStrategies.GroupedMessage();
        var a = Create("a");
        var nothing = Create(null);
        var b = Create("b");

        var bodyResult = bodies.Aggregate(bodies.Aggregate(bodies.Aggregate(null, a), nothing), b)!;
        bodies.OnCompletion(bodyResult, null);
        var messageResult = messages.Aggregate(messages.Aggregate(null, a), b)!;
        messages.OnCompletion(messageResult, null);

        Assert.Same(a, bodyResult);
        Assert.Equal(new object?[] { "a", "b" }, Assert.IsType<List<object?>>(bodyResult.Message.Body));
        Assert.Equal([a.Message, b.Message], Assert.IsType<List<Message>>(messageResult.Message.Body));
    }

    [Fact]
    public void String_strategy_joins_values_with_the_delimiter()
    {
        var bodies = AggregationStrategies.String(", ");
        var headers = AggregationStrategies.String(pick: Expr.Header("h"));
        var a = Create("a");
        a.Message.SetHeader("h", 1);
        var b = Create(null);
        var c = Create("c");
        c.Message.SetHeader("h", 3);

        var bodyResult = bodies.Aggregate(bodies.Aggregate(bodies.Aggregate(null, a), b), c)!;
        bodies.OnCompletion(bodyResult, null);
        var headerResult = headers.Aggregate(headers.Aggregate(null, Create("x", ("h", 1))), Create("y", ("h", 3)))!;
        headers.OnCompletion(headerResult, null);

        Assert.Equal("a, c", bodyResult.Message.Body);
        Assert.Equal("13", headerResult.Message.Body);
    }

    [Fact]
    public void Lambda_strategy_sees_failures_unless_it_propagates_them()
    {
        var failed = Create("bad");
        failed.Exception = new InvalidOperationException("boom");
        var seen = new List<string?>();
        var handling = AggregationStrategies.From((old, @new) =>
        {
            seen.Add(@new.Message.GetBody<string>());
            @new.Exception = null;
            return @new;
        });
        var propagating = AggregationStrategies.From(
            (old, @new) =>
            {
                seen.Add("propagating:" + @new.Message.GetBody<string>());
                return @new;
            },
            propagateException: true);

        Assert.Null(handling.Aggregate(null, failed)!.Exception);
        failed.Exception = new InvalidOperationException("again");
        var ok = Create("ok");
        Assert.Same(ok, propagating.Aggregate(null, ok));
        var result = propagating.Aggregate(ok, failed)!;

        Assert.Same(ok, result);
        Assert.Equal("again", result.Exception!.Message);
        Assert.Equal(["bad", "propagating:ok"], seen);
    }

    [Fact]
    public void Typed_body_strategy_folds_converted_bodies()
    {
        var strategy = AggregationStrategies.Body((string? all, int n) => all + n);

        var result = strategy.Aggregate(strategy.Aggregate(strategy.Aggregate(null, Create(1)), Create("2")), Create(3))!;

        Assert.Equal("123", result.Message.Body);
    }

    [Fact]
    public void Flexible_strategy_picks_filters_converts_and_stores()
    {
        var strategy = AggregationStrategies.Flexible<int>()
            .Pick(Expr.Header("n"))
            .Condition(Pred.From(e => e.Message.GetBody<string>() != "skip"))
            .AccumulateInCollection()
            .StoreInHeader("all");
        var first = Create("a", ("n", "1"));

        var result = strategy.Aggregate(null, first)!;
        result = strategy.Aggregate(result, Create("skip", ("n", 99)))!;
        result = strategy.Aggregate(result, Create("b", ("n", 2)))!;
        result = strategy.Aggregate(result, Create("c"))!;

        Assert.NotSame(first, result);
        Assert.Equal([1, 2], Assert.IsType<List<int>>(result.Message.GetHeader("all")));
    }

    [Fact]
    public void Flexible_strategy_keeps_the_latest_value_and_can_ignore_invalid_casts()
    {
        var completed = false;
        IAggregationStrategy strategy = AggregationStrategies.Flexible<int>()
            .StoreInProperty("last")
            .IgnoreInvalidCasts()
            .StoreNulls()
            .OnCompletion(_ => completed = true);

        var result = strategy.Aggregate(null, Create(5))!;
        Assert.Equal(5, result.GetProperty("last"));
        result = strategy.Aggregate(result, Create("not a number"))!;
        strategy.OnCompletion(result, null);

        Assert.Equal(0, result.GetProperty("last"));
        Assert.True(completed);
    }

    [Fact]
    public void Default_hooks_do_nothing()
    {
        IAggregationStrategy strategy = AggregationStrategies.UseLatest();
        var a = Create("a");

        Assert.False(strategy.CanPreComplete);
        Assert.False(strategy.PreComplete(null, a));
        Assert.Same(a, strategy.Aggregate(null, a, Create("input")));
        strategy.OnTimeout(a, 0, 1, TimeSpan.FromSeconds(1));
        strategy.OnCompletion(a, null);
        Assert.Equal("a", a.Message.Body);
    }

    [Fact]
    public async Task Grouped_exchange_strategy_in_a_route_gives_the_branch_exchanges()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Multicast(m => m.Transform(_ => "a").Transform(_ => "b"), aggregationStrategy: AggregationStrategies.GroupedExchange()));

        var reply = await Template.RequestBodyAsync("direct:start", "x");

        var grouped = Assert.IsType<List<Exchange>>(reply);
        Assert.Equal(["a", "b"], grouped.Select(e => e.Message.Body));
        Assert.Equal([0, 1], grouped.Select(e => e.GetProperty<int>(ExchangePropertyKeys.MulticastIndex)));
    }

    private Exchange Create(object? body, params (string Name, object? Value)[] headers)
    {
        var exchange = new Exchange(Context) { Message = { Body = body } };
        foreach (var (name, value) in headers)
        {
            exchange.Message.SetHeader(name, value);
        }

        return exchange;
    }
}
