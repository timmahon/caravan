using Caravan.Builder;
using Caravan.Impl;
using Caravan.Processors;

namespace Caravan.Core.Tests;

public class ResequencerTests : ContextTestBase
{
    private TestTimeProvider UseFakeTime()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        return time;
    }

    private Task Send(object? body, object? seq) => Template.SendBodyAndHeaderAsync("direct:start", body, "seq", seq);

    private Task StartResequencer(Action<ResequenceBuilder> configure, IExpression? expression = null) =>
        StartWithRoutes(b => b.From("direct:start").RouteId("r")
            .Resequence(expression ?? Expr.Header("seq"), configure, then => then.To("mock:result"))
            .To("mock:after"));

    private T Processor<T>() => ((Route)Context.GetRoute("r")!).RouteContext.Services.OfType<T>().Single();

    // ----- Batch -----

    [Fact]
    public async Task Batch_timeout_emits_the_batch_sorted()
    {
        var time = UseFakeTime();
        await StartWithRoutes(b => b.From("direct:start").Resequence(Expr.Body(), then => then.To("mock:result")));
        Mock("result").ExpectedBodiesReceived("a", "b", "c");

        await Template.SendBodyAsync("direct:start", "c");
        await Template.SendBodyAsync("direct:start", "a");
        await Template.SendBodyAsync("direct:start", "b");
        time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Equal(0, Mock("result").ReceivedCounter);
        time.Advance(TimeSpan.FromMilliseconds(1));

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Batch_size_emits_each_full_batch_at_once()
    {
        UseFakeTime();
        await StartResequencer(r => r.Batch(batchSize: 3));
        Mock("result").ExpectedBodiesReceived(1, 2, 3, 4, 5, 6);

        foreach (var n in new[] { 3, 1, 2, 6, 5, 4 })
        {
            await Send(n, n);
        }

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Batch_timeout_counts_from_the_first_exchange_of_the_batch()
    {
        var time = UseFakeTime();
        await StartResequencer(r => r.Batch(batchTimeout: TimeSpan.FromSeconds(1)));
        Mock("result").ExpectedBodiesReceived("a", "b");

        await Send("b", 2);
        time.Advance(TimeSpan.FromMilliseconds(600));
        await Send("a", 1);
        time.Advance(TimeSpan.FromMilliseconds(400));

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Batch_keeps_the_first_of_equal_keys_unless_duplicates_are_allowed()
    {
        var time = UseFakeTime();
        await StartResequencer(r => r.Batch());
        Mock("result").ExpectedBodiesReceived("y", "x");

        await Send("x", 2);
        await Send("y", 1);
        await Send("z", 2);
        time.Advance(TimeSpan.FromSeconds(1));

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Batch_allow_duplicates_keeps_equal_keys_in_arrival_order()
    {
        var time = UseFakeTime();
        await StartResequencer(r => r.Batch(allowDuplicates: true));
        Mock("result").ExpectedBodiesReceived("y", "x", "z");

        await Send("x", 2);
        await Send("y", 1);
        await Send("z", 2);
        time.Advance(TimeSpan.FromSeconds(1));

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Batch_reverse_emits_in_descending_order()
    {
        var time = UseFakeTime();
        await StartResequencer(r => r.Batch(reverse: true));
        Mock("result").ExpectedBodiesReceived(3, 2, 1);

        await Send(1, 1);
        await Send(3, 3);
        await Send(2, 2);
        time.Advance(TimeSpan.FromSeconds(1));

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Batch_sort_keys_of_different_types_are_compared_with_type_coercion()
    {
        UseFakeTime();
        await StartResequencer(r => r.Batch(batchSize: 3));
        Mock("result").ExpectedBodiesReceived("two", "ten", "hundred");

        await Send("ten", "10");
        await Send("hundred", 100L);
        await Send("two", 2);

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Batch_fails_invalid_exchanges()
    {
        UseFakeTime();
        await StartResequencer(r => r.Batch());

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "no seq");

        Assert.IsType<CaravanExchangeException>(exchange.Exception);
        Assert.StartsWith("Exchange is not valid to be used by the BatchProcessor", exchange.Exception!.Message);
    }

    [Fact]
    public async Task Batch_can_ignore_invalid_exchanges()
    {
        var time = UseFakeTime();
        await StartResequencer(r => r.Batch(ignoreInvalidExchanges: true), Expr.From(e => e.Message.GetHeader("seq") ?? throw new InvalidOperationException("no seq")));
        Mock("result").ExpectedBodiesReceived("a");

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "no seq");
        await Send("a", 1);
        time.Advance(TimeSpan.FromSeconds(1));

        Assert.False(exchange.IsFailed);
        Assert.Equal(2, Mock("after").ReceivedCounter);
        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Batch_incoming_exchanges_continue_at_once_and_stopping_emits_the_current_batch()
    {
        UseFakeTime();
        await StartResequencer(r => r.Batch());

        await Send("c", 3);
        await Send("a", 1);
        Assert.Equal(["c", "a"], Mock("after").ReceivedBodies);
        Assert.Equal(0, Mock("result").ReceivedCounter);
        Assert.Equal(2, Processor<BatchResequencerProcessor>().PendingExchangesSize);
        await Context.StopAsync();

        Assert.Equal(["a", "c"], Mock("result").ReceivedBodies);
    }

    [Fact]
    public async Task Batch_concurrent_producers_get_every_batch_sorted()
    {
        UseFakeTime();
        await StartResequencer(r => r.Batch(batchSize: 100));
        Mock("result").ExpectedMessageCount(1000);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(p => Task.Run(async () =>
        {
            for (var i = 0; i < 250; i++)
            {
                var n = (i * 4) + p;
                await Send(n, n);
            }
        })));

        await Mock("result").AssertIsSatisfiedAsync();
        var bodies = Mock("result").GetReceivedBodies<int>();
        Assert.Equal(Enumerable.Range(0, 1000), bodies.Order());
        foreach (var batch in bodies.Chunk(100))
        {
            Assert.Equal(batch.Order(), batch);
        }
    }

    // ----- Stream -----

    [Fact]
    public async Task Stream_delivers_in_order_once_the_first_exchange_timed_out()
    {
        var time = UseFakeTime();
        await StartResequencer(r => r.Stream());
        Mock("result").ExpectedBodiesReceived(1, 2, 3, 4);

        await Send(4, 4);
        await Send(1, 1);
        await Send(3, 3);
        await Send(2, 2);
        Assert.Equal(4, Processor<StreamResequencerProcessor>().PendingExchangesSize);
        Assert.Equal([4, 1, 3, 2], Mock("after").GetReceivedBodies<int>());
        time.Advance(TimeSpan.FromSeconds(1));

        await Mock("result").AssertIsSatisfiedAsync();
        Assert.Equal(4, Processor<StreamResequencerProcessor>().LastDelivered);
    }

    [Fact]
    public async Task Stream_delivers_at_once_when_a_gap_is_filled()
    {
        var time = UseFakeTime();
        await StartResequencer(r => r.Stream());
        Mock("result").ExpectedBodiesReceived(1);
        await Send(1, 1);
        time.Advance(TimeSpan.FromSeconds(1));
        await Mock("result").AssertIsSatisfiedAsync();
        Mock("result").Reset();
        Mock("result").ExpectedBodiesReceived(2, 3, 4);

        await Send(3, 3);
        await Send(4, 4);
        await Send(2, 2);

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Stream_gives_up_on_a_gap_after_the_timeout()
    {
        var time = UseFakeTime();
        await StartResequencer(r => r.Stream(timeout: TimeSpan.FromSeconds(2)));
        Mock("result").ExpectedBodiesReceived(1);
        await Send(1, 1);
        time.Advance(TimeSpan.FromSeconds(2));
        await Mock("result").AssertIsSatisfiedAsync();
        Mock("result").Reset();
        Mock("result").ExpectedBodiesReceived(3, 4);
        var processor = Processor<StreamResequencerProcessor>();
        await TestHelpers.WaitUntilAsync(() => processor.PendingExchangesSize == 0);

        await Send(4, 4);
        await Send(3, 3);
        Assert.Equal(2, processor.PendingExchangesSize);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, processor.PendingExchangesSize);
        time.Advance(TimeSpan.FromSeconds(1));

        await Mock("result").AssertIsSatisfiedAsync();
        await TestHelpers.WaitUntilAsync(() => processor.PendingExchangesSize == 0);
    }

    [Fact]
    public async Task Stream_drops_duplicate_sequence_numbers_waiting_for_delivery()
    {
        var time = UseFakeTime();
        await StartResequencer(r => r.Stream());
        Mock("result").ExpectedBodiesReceived("first", "two");

        await Send("first", 1);
        await Send("second", 1);
        await Send("two", 2);
        time.Advance(TimeSpan.FromSeconds(1));

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Stream_reject_old_fails_exchanges_older_than_the_last_delivered_one()
    {
        var time = UseFakeTime();
        await StartResequencer(r => r.Stream(rejectOld: true));
        Mock("result").ExpectedBodiesReceived(1, 3);
        await Send(1, 1);
        await Send(3, 3);
        time.Advance(TimeSpan.FromSeconds(1));
        await TestHelpers.WaitUntilAsync(() => Processor<StreamResequencerProcessor>().LastDelivered == 3);

        var late = await Template.SendAsync("direct:start", e => e.Message.SetHeader("seq", 2));

        Assert.IsType<MessageRejectedException>(late.Exception);
        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Stream_delivers_old_exchanges_late_without_reject_old()
    {
        var time = UseFakeTime();
        await StartResequencer(r => r.Stream());
        Mock("result").ExpectedBodiesReceived(1, 3, 2);
        await Send(1, 1);
        await Send(3, 3);
        time.Advance(TimeSpan.FromSeconds(1));
        await TestHelpers.WaitUntilAsync(() => Processor<StreamResequencerProcessor>().LastDelivered == 3);

        await Send(2, 2);
        time.Advance(TimeSpan.FromSeconds(1));

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData(null)]
    public async Task Stream_fails_exchanges_without_a_valid_sequence_number(string? seq)
    {
        UseFakeTime();
        await StartResequencer(r => r.Stream());

        var exchange = await Template.SendAsync("direct:start", e => e.Message.SetHeader("seq", seq));

        Assert.IsType<CaravanExchangeException>(exchange.Exception);
        Assert.StartsWith("Error processing Exchange in StreamResequencer", exchange.Exception!.Message);
    }

    [Fact]
    public async Task Stream_can_ignore_invalid_exchanges()
    {
        var time = UseFakeTime();
        await StartResequencer(r => r.Stream(ignoreInvalidExchanges: true));
        Mock("result").ExpectedBodiesReceived(1);

        var exchange = await Template.SendAsync("direct:start", e => e.Message.SetHeader("seq", "abc"));
        await Send(1, 1);
        time.Advance(TimeSpan.FromSeconds(1));

        Assert.False(exchange.IsFailed);
        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Stream_senders_wait_while_the_resequencer_is_full()
    {
        var time = UseFakeTime();
        await StartResequencer(r => r.Stream(capacity: 2));
        Mock("result").ExpectedBodiesReceived(5, 7, 9);

        await Send(5, 5);
        await Send(7, 7);
        var blocked = Send(9, 9);
        Assert.False(blocked.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(1));
        await blocked.Bounded();
        time.Advance(TimeSpan.FromSeconds(1));

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Stream_stopping_delivers_the_held_exchanges_in_order()
    {
        UseFakeTime();
        await StartResequencer(r => r.Stream());

        await Send(5, 5);
        await Send(2, 2);
        await Send(3, 3);
        await Context.StopAsync();

        Assert.Equal([2, 3, 5], Mock("result").GetReceivedBodies<int>());
    }

    [Fact]
    public async Task Stream_concurrent_producers_are_delivered_in_order()
    {
        var time = UseFakeTime();
        await StartResequencer(r => r.Stream());
        Mock("result").ExpectedMessageCount(1000);
        var numbers = Enumerable.Range(0, 1000).OrderBy(_ => Random.Shared.Next()).ToArray();

        await Task.WhenAll(numbers.Chunk(125).Select(chunk => Task.Run(async () =>
        {
            foreach (var n in chunk)
            {
                await Send(n, n);
            }
        })));
        Assert.Equal(1000, Processor<StreamResequencerProcessor>().PendingExchangesSize);
        time.Advance(TimeSpan.FromSeconds(1));

        await Mock("result").AssertIsSatisfiedAsync();
        Assert.Equal(Enumerable.Range(0, 1000), Mock("result").GetReceivedBodies<int>());
    }

    [Fact]
    public async Task Resequencer_options_are_validated()
    {
        await Assert.ThrowsAsync<FailedToCreateRouteException>(() => StartResequencer(r => r.Stream(capacity: 0)));
    }

    [Fact]
    public async Task Batch_options_are_validated()
    {
        await Assert.ThrowsAsync<FailedToCreateRouteException>(() => StartResequencer(r => r.Batch(batchSize: 0)));
    }
}
