using System.Collections.Concurrent;
using Caravan.Aggregation;
using Caravan.Builder;
using Caravan.Impl;
using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Core.Tests;

public class AggregatorTests : ContextTestBase
{
    private static string Bodies(Exchange exchange) => string.Join(",", (IEnumerable<object?>)exchange.Message.Body!);

    private TestTimeProvider UseFakeTime()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        return time;
    }

    private Task Send(object? body, object? id) => Template.SendBodyAndHeaderAsync("direct:start", body, "id", id);

    private AggregateProcessor Processor(string routeId = "r") =>
        ((Route)Context.GetRoute(routeId)!).RouteContext.Services.OfType<AggregateProcessor>().Single();

    private Task StartAggregator(Action<AggregateBuilder> options, IAggregationStrategy? strategy = null, Action<RouteSteps>? after = null) =>
        StartWithRoutes(b =>
        {
            var route = b.From("direct:start").RouteId("r")
                .Aggregate(Expr.Header("id"), strategy ?? AggregationStrategies.GroupedBody(), options, then => then.To("mock:result"));
            after?.Invoke(route);
        });

    // ----- Completion size -----

    [Fact]
    public async Task Completion_size_sends_the_aggregated_exchange_with_the_aggregation_properties()
    {
        await StartAggregator(a => a.CompletionSize(3));

        await Send("a", "A");
        await Send("b", "A");
        Assert.Equal(0, Mock("result").ReceivedCounter);
        await Send("c", "A");

        var result = Assert.Single(Mock("result").ReceivedExchanges);
        Assert.Equal("a,b,c", Bodies(result));
        Assert.Equal(3, result.GetProperty(ExchangePropertyKeys.AggregatedSize));
        Assert.Equal(AggregationCompletedBy.Size, result.GetProperty(ExchangePropertyKeys.AggregatedCompletedBy));
        Assert.Equal("A", result.GetProperty(ExchangePropertyKeys.AggregatedCorrelationKey));
        Assert.Null(result.GetProperty(ExchangePropertyKeys.AggregatedTimeout));
    }

    [Fact]
    public async Task Groups_are_kept_per_correlation_key()
    {
        await StartAggregator(a => a.CompletionSize(2));

        await Send("a1", "A");
        await Send("b1", "B");
        await Send("c1", "C");
        await Send("b2", "B");
        await Send("a2", "A");

        Assert.Equal(["b1,b2", "a1,a2"], Mock("result").ReceivedExchanges.Select(Bodies));
        Assert.Equal(["B", "A"], Mock("result").ReceivedExchanges.Select(e => e.GetProperty(ExchangePropertyKeys.AggregatedCorrelationKey)));
        Assert.Equal(["C"], await Processor().Repository.GetKeysAsync());
    }

    [Fact]
    public async Task Completion_size_expression_takes_precedence_over_the_fixed_size()
    {
        await StartAggregator(a => a.CompletionSize(10).CompletionSize(Expr.Header("size")));

        await Template.SendBodyAndHeadersAsync("direct:start", "a", new Dictionary<string, object?> { ["id"] = "A", ["size"] = 2 });
        await Template.SendBodyAndHeadersAsync("direct:start", "b", new Dictionary<string, object?> { ["id"] = "A", ["size"] = 2 });
        await Template.SendBodyAndHeadersAsync("direct:start", "c", new Dictionary<string, object?> { ["id"] = "B", ["size"] = "1" });

        Assert.Equal(["a,b", "c"], Mock("result").ReceivedExchanges.Select(Bodies));
    }

    [Fact]
    public async Task The_incoming_exchange_continues_unchanged_and_the_completed_exchange_is_new()
    {
        var order = new ConcurrentQueue<string>();
        await StartWithRoutes(b => b.From("direct:start")
            .Aggregate(
                Expr.Header("id"),
                AggregationStrategies.GroupedBody(),
                a => a.CompletionSize(2),
                then => then
                    .Process(e =>
                    {
                        Assert.NotNull(e.UnitOfWork);
                        order.Enqueue("aggregated:" + Bodies(e));
                    })
                    .To("mock:result"))
            .Process(e => order.Enqueue("after:" + e.Message.Body))
            .To("mock:after"));

        var first = await Template.SendAsync("direct:start", e =>
        {
            e.Message.Body = "a";
            e.Message.SetHeader("id", "A");
        });
        var second = await Template.SendAsync("direct:start", e =>
        {
            e.Message.Body = "b";
            e.Message.SetHeader("id", "A");
        });

        // Without parallel processing the completed group is sent on the completing thread, before the incoming exchange continues.
        Assert.Equal(["after:a", "aggregated:a,b", "after:b"], order);
        Assert.Equal("b", second.Message.Body);
        Assert.False(second.IsFailed);
        var result = Assert.Single(Mock("result").ReceivedExchanges);
        Assert.NotEqual(first.ExchangeId, result.ExchangeId);
        Assert.NotEqual(second.ExchangeId, result.ExchangeId);
        Assert.Equal(first.ExchangeId, result.GetProperty(ExchangePropertyKeys.CorrelationId));
    }

    [Fact]
    public async Task Exchanges_are_aggregated_as_copies()
    {
        await StartAggregator(a => a.CompletionSize(2), after: r => r.SetBody(_ => "changed"));

        await Send("a", "A");
        await Send("b", "A");

        Assert.Equal("a,b", Bodies(Mock("result").ReceivedExchanges.Single()));
    }

    [Fact]
    public async Task A_failure_while_sending_the_completed_exchange_does_not_fail_the_incoming_exchange()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Aggregate(Expr.Header("id"), AggregationStrategies.GroupedBody(), a => a.CompletionSize(1), then => then.ThrowException(new InvalidOperationException("boom")))
            .To("mock:after"));

        var exchange = await Template.SendAsync("direct:start", e => e.Message.SetHeader("id", "A"));

        Assert.False(exchange.IsFailed);
        Assert.Equal(1, Mock("after").ReceivedCounter);
    }

    // ----- Completion timeout -----

    [Fact]
    public async Task Completion_timeout_is_restarted_by_each_exchange_of_the_group()
    {
        var time = UseFakeTime();
        await StartAggregator(a => a.CompletionTimeout(TimeSpan.FromSeconds(3)));

        await Send("a1", "A");
        await time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));
        await Send("b1", "B");
        await time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));
        await Send("a2", "A");
        await time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));
        await time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));

        // B timed out at 4s; A was restarted at 2s and times out at 5s.
        Assert.Equal(["b1"], Mock("result").ReceivedExchanges.Select(Bodies));
        await time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(["b1", "a1,a2"], Mock("result").ReceivedExchanges.Select(Bodies));
        var a = Mock("result").ReceivedExchanges[1];
        Assert.Equal(AggregationCompletedBy.Timeout, a.GetProperty(ExchangePropertyKeys.AggregatedCompletedBy));
        Assert.Equal(TimeSpan.FromSeconds(3), a.GetProperty(ExchangePropertyKeys.AggregatedTimeout));
        Assert.Equal(2, a.GetProperty(ExchangePropertyKeys.AggregatedSize));
    }

    [Fact]
    public async Task Completion_timeout_is_checked_at_the_checker_interval()
    {
        var time = UseFakeTime();
        await StartAggregator(a => a.CompletionTimeout(TimeSpan.FromMilliseconds(500)).CompletionTimeoutCheckerInterval(TimeSpan.FromSeconds(2)));

        await Send("a", "A");
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0, Mock("result").ReceivedCounter);
        await time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1, Mock("result").ReceivedCounter);
    }

    [Fact]
    public async Task Completion_timeout_expression_sets_the_timeout_per_exchange()
    {
        var time = UseFakeTime();
        await StartAggregator(a => a.CompletionTimeout(Expr.Header("timeout")));

        await Template.SendBodyAndHeadersAsync("direct:start", "a", new Dictionary<string, object?> { ["id"] = "A", ["timeout"] = 1000 });
        await Template.SendBodyAndHeadersAsync("direct:start", "b", new Dictionary<string, object?> { ["id"] = "B", ["timeout"] = TimeSpan.FromSeconds(2) });
        await time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(["a"], Mock("result").ReceivedExchanges.Select(Bodies));
        await time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(["a", "b"], Mock("result").ReceivedExchanges.Select(Bodies));
        Assert.Equal(TimeSpan.FromSeconds(2), Mock("result").ReceivedExchanges[1].GetProperty(ExchangePropertyKeys.AggregatedTimeout));
    }

    [Fact]
    public async Task Timeout_calls_the_strategy_on_timeout_hook()
    {
        var time = UseFakeTime();
        var strategy = new RecordingStrategy();
        await StartAggregator(a => a.CompletionTimeout(TimeSpan.FromSeconds(1)), strategy);

        await Send("a", "A");
        await time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));

        var (exchange, timeout) = Assert.Single(strategy.Timeouts);
        Assert.Equal("a", exchange.Message.Body);
        Assert.Equal(TimeSpan.FromSeconds(1), timeout);
        Assert.Equal(1, Mock("result").ReceivedCounter);
        Assert.Single(strategy.Completed);
    }

    [Fact]
    public async Task Discard_on_completion_timeout_drops_the_group_but_still_calls_on_timeout()
    {
        var time = UseFakeTime();
        var strategy = new RecordingStrategy();
        await StartAggregator(a => a.CompletionSize(3).CompletionTimeout(TimeSpan.FromSeconds(1)).DiscardOnCompletionTimeout(), strategy);

        await Send("a", "A");
        await time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));
        await Send("b1", "B");
        await Send("b2", "B");
        await Send("b3", "B");

        Assert.Single(strategy.Timeouts);
        Assert.Equal(["b1"], Mock("result").ReceivedBodies);
        Assert.Empty(await Processor().Repository.GetKeysAsync());
    }

    // ----- Completion interval -----

    [Fact]
    public async Task Completion_interval_completes_all_groups_periodically()
    {
        var time = UseFakeTime();
        await StartAggregator(a => a.CompletionInterval(TimeSpan.FromSeconds(5)));

        await Send("a1", "A");
        await Send("b1", "B");
        await Send("a2", "A");
        time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(0, Mock("result").ReceivedCounter);
        await time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(["a1,a2", "b1"], Mock("result").ReceivedExchanges.Select(Bodies).Order());
        Assert.All(Mock("result").ReceivedExchanges, e => Assert.Equal(AggregationCompletedBy.Interval, e.GetProperty(ExchangePropertyKeys.AggregatedCompletedBy)));

        await Send("a3", "A");
        await time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("a3", Bodies(Mock("result").ReceivedExchanges[2]));
    }

    [Fact]
    public async Task Completion_interval_and_timeout_cannot_be_combined()
    {
        var e = await Assert.ThrowsAsync<FailedToCreateRouteException>(() => StartAggregator(a => a.CompletionInterval(TimeSpan.FromSeconds(1)).CompletionTimeout(TimeSpan.FromSeconds(1))));
        Assert.Contains("Only one of completionInterval or completionTimeout", e.Message);
    }

    [Fact]
    public async Task A_completion_condition_is_required()
    {
        var e = await Assert.ThrowsAsync<FailedToCreateRouteException>(() => StartAggregator(a => a.EagerCheckCompletion()));
        Assert.Contains("At least one of the completions options", e.Message);
    }

    [Fact]
    public async Task An_aggregation_strategy_is_required()
    {
        await Assert.ThrowsAsync<FailedToCreateRouteException>(() => StartWithRoutes(b => b.From("direct:start")
            .Aggregate(Expr.Header("id"), null, a => a.CompletionSize(1), then => then.To("mock:result"))));
    }

    // ----- Completion predicate and eager check -----

    [Fact]
    public async Task Completion_predicate_is_checked_on_the_aggregated_exchange()
    {
        await StartAggregator(
            a => a.CompletionPredicate((string s) => s.EndsWith("END", StringComparison.Ordinal)),
            AggregationStrategies.From((old, @new) =>
            {
                if (old is not null)
                {
                    old.Message.Body = old.Message.Body + "+" + @new.Message.Body;
                }

                return old ?? @new;
            }));

        await Send("A", "x");
        await Send("B", "x");
        await Send("END", "x");

        var result = Assert.Single(Mock("result").ReceivedExchanges);
        Assert.Equal("A+B+END", result.Message.Body);
        Assert.Equal(AggregationCompletedBy.Predicate, result.GetProperty(ExchangePropertyKeys.AggregatedCompletedBy));
    }

    [Fact]
    public async Task Eager_check_completion_tests_the_incoming_exchange_before_aggregating_it()
    {
        var concat = AggregationStrategies.From((old, @new) =>
        {
            if (old is not null)
            {
                old.Message.Body = old.Message.Body + "+" + @new.Message.Body;
            }

            return old ?? @new;
        });
        var sizes = new List<object?>();
        await StartAggregator(
            a => a.EagerCheckCompletion().CompletionPredicate(e =>
            {
                sizes.Add(e.GetProperty(ExchangePropertyKeys.AggregatedSize));
                return Equals(e.Message.Body, "END");
            }),
            concat);

        await Send("A", "x");
        await Send("B", "x");
        await Send("END", "x");

        Assert.Equal("A+B+END", Mock("result").ReceivedExchanges.Single().Message.Body);
        Assert.Equal([1, 2, 3], sizes);
    }

    [Fact]
    public async Task Without_eager_check_a_predicate_on_the_incoming_body_never_matches_the_aggregated_body()
    {
        await StartAggregator(
            a => a.CompletionPredicate(e => Equals(e.Message.Body, "END")),
            AggregationStrategies.From((old, @new) =>
            {
                if (old is not null)
                {
                    old.Message.Body = old.Message.Body + "+" + @new.Message.Body;
                }

                return old ?? @new;
            }));

        await Send("A", "x");
        await Send("END", "x");

        Assert.Equal(0, Mock("result").ReceivedCounter);
    }

    [Fact]
    public async Task A_strategy_that_is_a_predicate_is_the_completion_predicate()
    {
        await StartAggregator(_ => { }, new PredicateStrategy());

        await Send("a", "A");
        await Send("stop", "A");

        Assert.Equal("a,stop", Bodies(Mock("result").ReceivedExchanges.Single()));
    }

    // ----- Batch consumer, new correlation group, forced completion -----

    [Fact]
    public async Task Completion_from_batch_consumer_completes_all_groups_of_the_batch()
    {
        await StartAggregator(a => a.CompletionFromBatchConsumer());

        for (var i = 0; i < 4; i++)
        {
            await Template.SendAsync("direct:start", e =>
            {
                e.Message.Body = i;
                e.Message.SetHeader("id", i % 2 == 0 ? "even" : "odd");
                e.SetProperty(ExchangePropertyKeys.BatchSize, 4);
                e.SetProperty(ExchangePropertyKeys.BatchIndex, i);
            });
        }

        Assert.Equal(["0,2", "1,3"], Mock("result").ReceivedExchanges.Select(Bodies).Order());
        Assert.All(Mock("result").ReceivedExchanges, e => Assert.Equal(AggregationCompletedBy.Consumer, e.GetProperty(ExchangePropertyKeys.AggregatedCompletedBy)));
        Assert.Empty(await Processor().Repository.GetKeysAsync());
    }

    [Fact]
    public async Task Completion_from_batch_consumer_uses_the_batch_complete_property()
    {
        await StartAggregator(a => a.CompletionFromBatchConsumer());

        await Send("a", "A");
        await Send("b", "B");
        await Template.SendAsync("direct:start", e =>
        {
            e.Message.Body = "c";
            e.Message.SetHeader("id", "A");
            e.SetProperty(ExchangePropertyKeys.BatchComplete, true);
        });

        Assert.Equal(["a,c", "b"], Mock("result").ReceivedExchanges.Select(Bodies).Order());
    }

    [Fact]
    public async Task Completion_on_new_correlation_group_completes_the_previous_groups()
    {
        await StartAggregator(a => a.CompletionOnNewCorrelationGroup());

        await Send("a1", "A");
        await Send("a2", "A");
        Assert.Equal(0, Mock("result").ReceivedCounter);
        await Send("b1", "B");
        await Send("c1", "C");

        Assert.Equal(["a1,a2", "b1"], Mock("result").ReceivedExchanges.Select(Bodies));
        Assert.All(Mock("result").ReceivedExchanges, e => Assert.Equal(AggregationCompletedBy.Force, e.GetProperty(ExchangePropertyKeys.AggregatedCompletedBy)));
    }

    [Fact]
    public async Task Complete_all_groups_header_completes_the_groups_without_aggregating_the_exchange()
    {
        await StartAggregator(a => a.CompletionSize(10));

        await Send("a", "A");
        await Send("b", "B");
        await Template.SendBodyAndHeadersAsync("direct:start", "signal", new Dictionary<string, object?>
        {
            ["id"] = "A",
            [ExchangePropertyKeys.AggregationCompleteAllGroups] = true,
        });

        Assert.Equal(["a", "b"], Mock("result").ReceivedExchanges.Select(Bodies).Order());
        Assert.Empty(await Processor().Repository.GetKeysAsync());
    }

    [Fact]
    public async Task Complete_all_groups_inclusive_header_aggregates_the_exchange_first()
    {
        await StartAggregator(a => a.CompletionSize(10));

        await Send("a", "A");
        await Send("b", "B");
        await Template.SendBodyAndHeadersAsync("direct:start", "last", new Dictionary<string, object?>
        {
            ["id"] = "A",
            [ExchangePropertyKeys.AggregationCompleteAllGroupsInclusive] = true,
        });

        Assert.Equal(["a,last", "b"], Mock("result").ReceivedExchanges.Select(Bodies).Order());
    }

    [Fact]
    public async Task A_strategy_can_complete_the_current_group_or_all_groups()
    {
        await StartAggregator(
            a => a.CompletionSize(10),
            AggregationStrategies.From((old, @new) =>
            {
                var result = old ?? @new;
                if (old is not null)
                {
                    old.Message.Body = old.Message.Body + "+" + @new.Message.Body;
                }

                switch (@new.Message.Body)
                {
                    case "current":
                        result.SetProperty(ExchangePropertyKeys.AggregationCompleteCurrentGroup, true);
                        break;
                    case "all":
                        result.SetProperty(ExchangePropertyKeys.AggregationCompleteAllGroups, true);
                        break;
                }

                return result;
            }));

        await Send("a", "A");
        await Send("current", "A");
        Assert.Equal(AggregationCompletedBy.Strategy, Mock("result").ReceivedExchanges.Single().GetProperty(ExchangePropertyKeys.AggregatedCompletedBy));

        await Send("b", "B");
        await Send("c", "C");
        await Send("all", "C");

        Assert.Equal(["a+current", "b", "c+all"], Mock("result").ReceivedBodies);
        Assert.All(Mock("result").ReceivedExchanges.Skip(1), e => Assert.Equal(AggregationCompletedBy.Force, e.GetProperty(ExchangePropertyKeys.AggregatedCompletedBy)));
        Assert.Empty(await Processor().Repository.GetKeysAsync());
    }

    [Fact]
    public async Task Pre_completion_strategy_completes_the_group_before_aggregating_the_new_exchange()
    {
        var time = UseFakeTime();
        await StartAggregator(a => a.CompletionTimeout(TimeSpan.FromSeconds(1)), new PreCompleteOnNewPrefixStrategy());

        await Send("x1", "A");
        await Send("x2", "A");
        await Send("y1", "A");
        Assert.Equal("x1+x2", Mock("result").ReceivedExchanges.Single().Message.Body);
        Assert.Equal(AggregationCompletedBy.Strategy, Mock("result").ReceivedExchanges[0].GetProperty(ExchangePropertyKeys.AggregatedCompletedBy));
        Assert.Equal(2, Mock("result").ReceivedExchanges[0].GetProperty(ExchangePropertyKeys.AggregatedSize));

        // The new group started with y1 and still completes by timeout.
        await time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(["x1+x2", "y1"], Mock("result").ReceivedBodies);
    }

    [Fact]
    public async Task Pre_completion_strategy_alone_is_a_completion_condition()
    {
        await StartAggregator(_ => { }, new PreCompleteOnNewPrefixStrategy());

        await Send("x1", "A");
        await Send("y1", "A");

        Assert.Equal(["x1"], Mock("result").ReceivedBodies);
    }

    // ----- Correlation keys -----

    [Fact]
    public async Task An_invalid_correlation_key_fails_the_exchange()
    {
        await StartAggregator(a => a.CompletionSize(1));

        var missing = await Template.SendAsync("direct:start", e => e.Message.Body = "a");
        var blank = await Template.SendAsync("direct:start", e => e.Message.SetHeader("id", " "));

        Assert.IsType<CaravanExchangeException>(missing.Exception);
        Assert.StartsWith("Invalid correlation key", missing.Exception!.Message);
        Assert.IsType<CaravanExchangeException>(blank.Exception);
        Assert.Equal(0, Mock("result").ReceivedCounter);
    }

    [Fact]
    public async Task Invalid_correlation_keys_can_be_ignored()
    {
        await StartAggregator(a => a.CompletionSize(1).IgnoreInvalidCorrelationKeys(), after: r => r.To("mock:after"));

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "a");
        await Send("b", "B");

        Assert.False(exchange.IsFailed);
        Assert.Equal(["b"], Mock("result").ReceivedExchanges.Select(Bodies));
        Assert.Equal(2, Mock("after").ReceivedCounter);
    }

    [Fact]
    public async Task Close_correlation_key_on_completion_rejects_late_exchanges()
    {
        await StartAggregator(a => a.CompletionSize(2).CloseCorrelationKeyOnCompletion(0));

        await Send("a1", "A");
        await Send("a2", "A");
        var late = await Template.SendAsync("direct:start", e =>
        {
            e.Message.Body = "a3";
            e.Message.SetHeader("id", "A");
        });
        await Send("b1", "B");

        var exception = Assert.IsType<ClosedCorrelationKeyException>(late.Exception);
        Assert.Equal("A", exception.CorrelationKey);
        Assert.StartsWith("The correlation key [A] has been closed", exception.Message);
        Assert.Equal(["a1,a2"], Mock("result").ReceivedExchanges.Select(Bodies));

        Processor().ClearClosedCorrelationKeys();
        await Send("a4", "A");
        await Send("a5", "A");
        Assert.Equal(2, Mock("result").ReceivedCounter);
    }

    [Fact]
    public async Task Closed_correlation_keys_are_bounded_by_the_capacity()
    {
        await StartAggregator(a => a.CompletionSize(1).CloseCorrelationKeyOnCompletion(1));

        await Send("a", "A");
        await Send("b", "B");

        // A was evicted when B was closed.
        var closed = await Template.SendAsync("direct:start", e => e.Message.SetHeader("id", "B"));
        var again = await Template.SendAsync("direct:start", e => e.Message.SetHeader("id", "A"));

        Assert.False(again.IsFailed);
        Assert.IsType<ClosedCorrelationKeyException>(closed.Exception);
        Assert.Equal(1, Processor().ClosedCorrelationKeysCount);
    }

    // ----- Aggregation failures -----

    [Fact]
    public async Task A_failing_strategy_fails_the_incoming_exchange_and_keeps_the_group()
    {
        await StartAggregator(a => a.CompletionSize(2), new FailOnBadStrategy());

        await Send("a", "A");
        var failed = await Template.SendAsync("direct:start", e =>
        {
            e.Message.Body = "bad";
            e.Message.SetHeader("id", "A");
        });
        await Send("b", "A");

        var exception = Assert.IsType<CaravanExchangeException>(failed.Exception);
        Assert.StartsWith("Error occurred during aggregation", exception.Message);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal(["a,b"], Mock("result").ReceivedBodies.Select(b => string.Join(",", (IEnumerable<object?>)b!)));
    }

    [Fact]
    public async Task Discard_on_aggregation_failure_drops_the_group()
    {
        await StartAggregator(a => a.CompletionSize(2).DiscardOnAggregationFailure(), new FailOnBadStrategy());

        await Send("bad", "A");
        await Send("a1", "A");
        var failed = await Template.SendAsync("direct:start", e =>
        {
            e.Message.Body = "bad";
            e.Message.SetHeader("id", "A");
        });
        await Send("a2", "A");
        await Send("a3", "A");

        Assert.False(failed.IsFailed);
        Assert.Equal(["a2,a3"], Mock("result").ReceivedBodies.Select(b => string.Join(",", (IEnumerable<object?>)b!)));
    }

    [Fact]
    public async Task A_strategy_returning_null_fails_the_incoming_exchange()
    {
        await StartAggregator(a => a.CompletionSize(2), AggregationStrategies.From((_, _) => null));

        var exchange = await Template.SendAsync("direct:start", e => e.Message.SetHeader("id", "A"));

        Assert.Contains("returned null which is not allowed", exchange.Exception!.Message);
    }

    // ----- Stop and shutdown -----

    [Fact]
    public async Task Force_completion_on_stop_sends_the_pending_groups()
    {
        await StartAggregator(a => a.CompletionSize(10).ForceCompletionOnStop());

        await Send("a1", "A");
        await Send("b1", "B");
        await Send("a2", "A");
        await Context.StopAsync();

        Assert.Equal(["a1,a2", "b1"], Mock("result").ReceivedExchanges.Select(Bodies).Order());
        Assert.All(Mock("result").ReceivedExchanges, e => Assert.Equal(AggregationCompletedBy.Force, e.GetProperty(ExchangePropertyKeys.AggregatedCompletedBy)));
    }

    [Fact]
    public async Task Pending_groups_are_dropped_on_stop_by_default()
    {
        await StartAggregator(a => a.CompletionSize(10));

        await Send("a1", "A");
        await Context.StopAsync();

        Assert.Equal(0, Mock("result").ReceivedCounter);
    }

    [Fact]
    public async Task Stopping_a_route_forces_completion_but_suspending_does_not()
    {
        await StartAggregator(a => a.CompletionSize(10).ForceCompletionOnStop());

        await Send("a1", "A");
        await Context.SuspendRouteAsync("r");
        Assert.Equal(0, Mock("result").ReceivedCounter);
        await Context.ResumeRouteAsync("r");
        await Context.StopRouteAsync("r");

        Assert.Equal(["a1"], Mock("result").ReceivedExchanges.Select(Bodies));
    }

    [Fact]
    public async Task Complete_all_on_stop_waits_for_the_groups_to_complete_by_timeout()
    {
        var time = UseFakeTime();
        await StartAggregator(a => a.CompletionTimeout(TimeSpan.FromSeconds(3)).CompleteAllOnStop());

        await Send("a1", "A");
        var processor = Processor();
        var stop = Context.StopAsync();
        await TestHelpers.WaitUntilAsync(() => processor.PendingExchangesSize == 1);
        Assert.False(stop.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(3));
        await stop.Bounded();

        Assert.Equal(["a1"], Mock("result").ReceivedExchanges.Select(Bodies));
        Assert.Equal(AggregationCompletedBy.Timeout, Mock("result").ReceivedExchanges[0].GetProperty(ExchangePropertyKeys.AggregatedCompletedBy));
    }

    [Fact]
    public async Task Stopping_waits_for_completed_groups_sent_in_parallel()
    {
        var release = TestHelpers.NewSignal();
        var started = TestHelpers.NewSignal();
        await StartWithRoutes(b => b.From("direct:start")
            .Aggregate(
                Expr.Header("id"),
                AggregationStrategies.GroupedBody(),
                a => a.CompletionSize(1).ParallelProcessing(),
                then => then
                    .Process(async _ =>
                    {
                        started.TrySetResult();
                        await release.Task.Bounded();
                    })
                    .To("mock:result")));

        await Send("a", "A");
        await started.Task.Bounded();
        var stop = Context.StopAsync();
        Assert.False(stop.IsCompleted);
        release.SetResult();
        await stop.Bounded();

        Assert.Equal(1, Mock("result").ReceivedCounter);
    }

    [Fact]
    public async Task Timeouts_are_restored_from_the_repository_when_the_aggregator_restarts()
    {
        var time = UseFakeTime();
        var repository = new KeepingRepository();
        await StartAggregator(a => a.CompletionTimeout(TimeSpan.FromSeconds(3)).AggregationRepository(repository));

        await Send("a1", "A");
        await Context.StopRouteAsync("r");
        Assert.Equal(1, repository.Count);
        await Context.StartRouteAsync("r");
        var timers = time.TimersCreated;
        time.Advance(TimeSpan.FromSeconds(3));
        await time.WaitForTimersAsync(timers + 1);
        await time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(["a1"], Mock("result").ReceivedExchanges.Select(Bodies));
    }

    // ----- Aggregate controller -----

    [Fact]
    public async Task Aggregate_controller_forces_completion_of_one_or_all_groups()
    {
        var controller = new DefaultAggregateController();
        Assert.Equal(0, await controller.ForceCompletionOfAllGroupsAsync());
        await StartAggregator(a => a.CompletionSize(10).AggregateController(controller));

        await Send("a", "A");
        await Send("b", "B");
        await Send("c", "C");

        Assert.Equal(1, await controller.ForceCompletionOfGroupAsync("B"));
        Assert.Equal(0, await controller.ForceCompletionOfGroupAsync("unknown"));
        Assert.Equal(["b"], Mock("result").ReceivedExchanges.Select(Bodies));
        Assert.Equal(AggregationCompletedBy.Force, Mock("result").ReceivedExchanges[0].GetProperty(ExchangePropertyKeys.AggregatedCompletedBy));

        Assert.Equal(2, await controller.ForceCompletionOfAllGroupsAsync());
        Assert.Equal(["a", "b", "c"], Mock("result").ReceivedExchanges.Select(Bodies).Order());
    }

    [Fact]
    public async Task Aggregate_controller_discards_groups()
    {
        var controller = new DefaultAggregateController();
        await StartAggregator(a => a.CompletionSize(10).AggregateController(controller));

        await Send("a", "A");
        await Send("b", "B");
        await Send("c", "C");

        Assert.Equal(1, await controller.ForceDiscardingOfGroupAsync("A"));
        Assert.Equal(2, await controller.ForceDiscardingOfAllGroupsAsync());
        Assert.Empty(await Processor().Repository.GetKeysAsync());
        Assert.Equal(0, Mock("result").ReceivedCounter);

        await Context.StopAsync();
        Assert.Null(controller.Processor);
    }

    // ----- Parallel processing and concurrency -----

    [Fact]
    public async Task Parallel_processing_sends_completed_groups_concurrently()
    {
        var probe = new ConcurrencyProbe();
        await StartWithRoutes(b => b.From("direct:start")
            .Aggregate(
                Expr.Header("id"),
                AggregationStrategies.GroupedBody(),
                a => a.CompletionSize(1).ParallelProcessing(maxDegreeOfParallelism: 2),
                then => then.Process(_ => probe.EnterAsync()).To("mock:result")));
        Mock("result").ExpectedMessageCount(3);

        await Send("a", "A");
        await Send("b", "B");
        await Send("c", "C");
        await probe.WaitForStartedAsync(2);
        Assert.Equal(2, probe.StartedCount);
        probe.Release(3);

        await Mock("result").AssertIsSatisfiedAsync();
        Assert.Equal(2, probe.Max);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Concurrent_producers_lose_and_duplicate_nothing(bool optimisticLocking, bool parallelProcessing)
    {
        const int producers = 8;
        const int perProducer = 250;
        const int keys = 10;
        const int size = 25;
        await StartAggregator(a => a
            .CompletionSize(size)
            .OptimisticLocking(optimisticLocking, new OptimisticLockRetryPolicy { RetryDelay = TimeSpan.Zero })
            .ParallelProcessing(parallelProcessing));
        Mock("result").ExpectedMessageCount(producers * perProducer / size);

        await Task.WhenAll(Enumerable.Range(0, producers).Select(p => Task.Run(async () =>
        {
            for (var i = 0; i < perProducer; i++)
            {
                var n = (p * perProducer) + i;
                await Send(n, n % keys);
            }
        })));

        await Mock("result").AssertIsSatisfiedAsync();
        var groups = Mock("result").ReceivedExchanges;
        Assert.All(groups, g =>
        {
            var bodies = ((IEnumerable<object?>)g.Message.Body!).Cast<int>().ToList();
            Assert.Equal(size, bodies.Count);
            Assert.Equal(size, g.GetProperty(ExchangePropertyKeys.AggregatedSize));
            Assert.All(bodies, n => Assert.Equal(g.GetProperty(ExchangePropertyKeys.AggregatedCorrelationKey), (n % keys).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        });
        var all = groups.SelectMany(g => ((IEnumerable<object?>)g.Message.Body!).Cast<int>()).Order().ToList();
        Assert.Equal(Enumerable.Range(0, producers * perProducer), all);
        Assert.Empty(await Processor().Repository.GetKeysAsync());
        await TestHelpers.WaitUntilAsync(() => Processor().InProgressCompleteExchanges == 0);
    }

    [Fact]
    public async Task Optimistic_locking_requires_a_repository_that_supports_it()
    {
        var e = await Assert.ThrowsAsync<FailedToCreateRouteException>(() =>
            StartAggregator(a => a.CompletionSize(2).OptimisticLocking().AggregationRepository(new KeepingRepository())));
        Assert.Contains("Optimistic locking", e.Message);
    }

    [Fact]
    public async Task Optimistic_locking_gives_up_after_the_maximum_retries()
    {
        var repository = new ConflictingRepository();
        await StartAggregator(a => a
            .CompletionSize(2)
            .AggregationRepository(repository)
            .OptimisticLocking(retryPolicy: new OptimisticLockRetryPolicy { MaximumRetries = 3, RetryDelay = TimeSpan.Zero }));

        var exchange = await Template.SendAsync("direct:start", e => e.Message.SetHeader("id", "A"));

        Assert.Equal("Exhausted optimistic locking retry attempts, tried 3 times. Exchange[" + exchange.ExchangeId + "]", exchange.Exception!.Message);
        Assert.IsType<OptimisticLockingException>(exchange.Exception.InnerException);
        Assert.Equal(3, repository.Attempts);
    }

    // ----- Repositories and references -----

    [Fact]
    public async Task Strategy_and_repository_can_be_looked_up_in_the_registry()
    {
        var repository = new MemoryAggregationRepository();
        Context.Registry.Bind("myStrategy", AggregationStrategies.GroupedBody());
        Context.Registry.Bind("myRepository", repository);
        await StartWithRoutes(b => b.From("direct:start").RouteId("r")
            .Aggregate(Expr.Header("id"), null, a => a.CompletionSize(2).AggregationStrategyRef("myStrategy").AggregationRepositoryRef("myRepository"), then => then.To("mock:result")));

        await Send("a", "A");
        Assert.Equal(1, repository.Count);
        await Send("b", "A");

        Assert.Equal("a,b", Bodies(Mock("result").ReceivedExchanges.Single()));
        Assert.Same(repository, Processor().Repository);
    }

    [Fact]
    public async Task Recoverable_repository_redelivers_failed_groups_then_moves_them_to_the_dead_letter_uri()
    {
        var time = UseFakeTime();
        var repository = new RecoverableRepository { MaximumRedeliveries = 2, DeadLetterUri = "mock:dead" };
        var attempts = new ConcurrentQueue<(object? Redelivered, object? Counter, object? Max)>();
        await StartAggregator(
            a => a.CompletionSize(2).AggregationRepository(repository),
            after: null);
        Mock("result").WhenAnyExchangeReceived(e =>
        {
            attempts.Enqueue((e.Message.GetHeader(CaravanHeaders.Redelivered), e.Message.GetHeader(CaravanHeaders.RedeliveryCounter), e.Message.GetHeader(CaravanHeaders.RedeliveryMaxCounter)));
            throw new InvalidOperationException("downstream down");
        });

        await Send("a", "A");
        await Send("b", "A");
        Assert.Single(attempts);
        var id = Assert.Single(repository.Completed.Keys);

        await time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(5));
        await time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(5));
        Assert.Equal([(null, null, null), (true, 1, 2), (true, 2, 2)], attempts);
        Assert.Equal(0, Mock("dead").ReceivedCounter);

        await time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(5));
        var dead = Assert.Single(Mock("dead").ReceivedExchanges);
        Assert.Equal(id, dead.ExchangeId);
        Assert.Equal("a,b", Bodies(dead));
        Assert.Equal(2, dead.Message.GetHeader(CaravanHeaders.RedeliveryCounter));
        Assert.Empty(repository.Completed);
        Assert.Equal(3, attempts.Count);
    }

    [Fact]
    public async Task Recoverable_repository_confirms_a_group_once_it_is_processed()
    {
        var time = UseFakeTime();
        var repository = new RecoverableRepository();
        var fail = true;
        await StartAggregator(a => a.CompletionSize(2).AggregationRepository(repository));
        Mock("result").WhenAnyExchangeReceived(_ =>
        {
            if (fail)
            {
                throw new InvalidOperationException("downstream down");
            }
        });

        await Send("a", "A");
        await Send("b", "A");
        Assert.Single(repository.Completed);
        fail = false;
        await time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(repository.Completed);
        Assert.Equal(2, Mock("result").ReceivedCounter);
        Assert.Equal(true, Mock("result").ReceivedExchanges[1].Message.GetHeader(CaravanHeaders.Redelivered));
    }

    [Fact]
    public async Task Recoverable_repository_with_maximum_redeliveries_requires_a_dead_letter_uri()
    {
        var e = await Assert.ThrowsAnyAsync<Exception>(() => StartAggregator(a => a.CompletionSize(2).AggregationRepository(new RecoverableRepository { MaximumRedeliveries = 2 })));
        Assert.Contains("deadLetterUri", e.ToString());
    }

    /// <summary>Records the hooks the aggregator calls.</summary>
    private sealed class RecordingStrategy : IAggregationStrategy
    {
        public ConcurrentQueue<(Exchange Exchange, TimeSpan Timeout)> Timeouts { get; } = new();

        public ConcurrentQueue<Exchange> Completed { get; } = new();

        public Exchange? Aggregate(Exchange? oldExchange, Exchange newExchange) => oldExchange ?? newExchange;

        public void OnTimeout(Exchange exchange, int index, int total, TimeSpan timeout)
        {
            Assert.Equal(-1, index);
            Assert.Equal(-1, total);
            Timeouts.Enqueue((exchange, timeout));
        }

        public void OnCompletion(Exchange exchange, Exchange? inputExchange) => Completed.Enqueue(exchange);
    }

    /// <summary>Groups bodies and completes when the aggregated body list ends with "stop".</summary>
    private sealed class PredicateStrategy : IAggregationStrategy, IPredicate
    {
        private readonly GroupedBodyAggregationStrategy _inner = new();

        public Exchange? Aggregate(Exchange? oldExchange, Exchange newExchange) => _inner.Aggregate(oldExchange, newExchange);

        public void OnCompletion(Exchange exchange, Exchange? inputExchange) => _inner.OnCompletion(exchange, inputExchange);

        public bool Matches(Exchange exchange) =>
            exchange.GetProperty(ExchangePropertyKeys.GroupedExchange) is List<object?> list && Equals(list[^1], "stop");
    }

    /// <summary>Concatenates bodies; completes the group when the first letter of the new body changes.</summary>
    private sealed class PreCompleteOnNewPrefixStrategy : IAggregationStrategy
    {
        public bool CanPreComplete => true;

        public bool PreComplete(Exchange? oldExchange, Exchange newExchange) =>
            oldExchange is not null && ((string)oldExchange.Message.Body!)[0] != ((string)newExchange.Message.Body!)[0];

        public Exchange? Aggregate(Exchange? oldExchange, Exchange newExchange)
        {
            if (oldExchange is null)
            {
                return newExchange;
            }

            oldExchange.Message.Body = oldExchange.Message.Body + "+" + newExchange.Message.Body;
            return oldExchange;
        }
    }

    /// <summary>Groups bodies; throws for the body "bad".</summary>
    private sealed class FailOnBadStrategy : IAggregationStrategy
    {
        private readonly GroupedBodyAggregationStrategy _inner = new();

        public Exchange? Aggregate(Exchange? oldExchange, Exchange newExchange) =>
            Equals(newExchange.Message.Body, "bad") ? throw new InvalidOperationException("bad body") : _inner.Aggregate(oldExchange, newExchange);

        public void OnCompletion(Exchange exchange, Exchange? inputExchange) => _inner.OnCompletion(exchange, inputExchange);
    }

    /// <summary>A plain repository that keeps its groups across restarts (like a durable store) and has no optimistic locking.</summary>
    private sealed class KeepingRepository : IAggregationRepository
    {
        private readonly ConcurrentDictionary<string, Exchange> _groups = new();

        public int Count => _groups.Count;

        public ValueTask AddAsync(ICaravanContext context, string key, Exchange exchange, CancellationToken cancellationToken = default)
        {
            _groups[key] = exchange.Copy();
            return ValueTask.CompletedTask;
        }

        public ValueTask<Exchange?> GetAsync(ICaravanContext context, string key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_groups.TryGetValue(key, out var e) ? e.Copy() : null);

        public ValueTask RemoveAsync(ICaravanContext context, string key, Exchange exchange, CancellationToken cancellationToken = default)
        {
            _groups.TryRemove(key, out _);
            return ValueTask.CompletedTask;
        }

        public ValueTask ConfirmAsync(ICaravanContext context, string exchangeId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<IReadOnlyCollection<string>> GetKeysAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<string>>([.. _groups.Keys]);
    }

    /// <summary>An optimistic repository that always reports a conflict.</summary>
    private sealed class ConflictingRepository : IOptimisticLockingAggregationRepository
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        public ValueTask AddAsync(ICaravanContext context, string key, Exchange? oldExchange, Exchange newExchange, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _attempts);
            throw new OptimisticLockingException();
        }

        public ValueTask AddAsync(ICaravanContext context, string key, Exchange exchange, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<Exchange?> GetAsync(ICaravanContext context, string key, CancellationToken cancellationToken = default) => ValueTask.FromResult<Exchange?>(null);

        public ValueTask RemoveAsync(ICaravanContext context, string key, Exchange exchange, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask ConfirmAsync(ICaravanContext context, string exchangeId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<IReadOnlyCollection<string>> GetKeysAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<string>>([]);
    }

    /// <summary>A memory repository that keeps completed exchanges until they are confirmed.</summary>
    private sealed class RecoverableRepository : IRecoverableAggregationRepository
    {
        private readonly MemoryAggregationRepository _groups = new();

        public ConcurrentDictionary<string, Exchange> Completed { get; } = new();

        public bool UseRecovery => true;

        public TimeSpan RecoveryInterval => TimeSpan.FromSeconds(5);

        public int MaximumRedeliveries { get; init; }

        public string? DeadLetterUri { get; init; }

        public ValueTask AddAsync(ICaravanContext context, string key, Exchange exchange, CancellationToken cancellationToken = default) =>
            _groups.AddAsync(context, key, exchange, cancellationToken);

        public ValueTask<Exchange?> GetAsync(ICaravanContext context, string key, CancellationToken cancellationToken = default) =>
            _groups.GetAsync(context, key, cancellationToken);

        public ValueTask RemoveAsync(ICaravanContext context, string key, Exchange exchange, CancellationToken cancellationToken = default)
        {
            Completed[exchange.ExchangeId] = exchange.Copy();
            return _groups.RemoveAsync(context, key, exchange, cancellationToken);
        }

        public ValueTask ConfirmAsync(ICaravanContext context, string exchangeId, CancellationToken cancellationToken = default)
        {
            Completed.TryRemove(exchangeId, out _);
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyCollection<string>> GetKeysAsync(CancellationToken cancellationToken = default) => _groups.GetKeysAsync(cancellationToken);

        public ValueTask<IReadOnlyCollection<string>> ScanAsync(ICaravanContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<string>>([.. Completed.Keys]);

        public ValueTask<Exchange?> RecoverAsync(ICaravanContext context, string exchangeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Completed.TryGetValue(exchangeId, out var e) ? e.Copy() : null);
    }
}
