using System.Runtime.CompilerServices;
using System.Text;
using Caravan.Builder;
using Caravan.Components.Mock;

namespace Caravan.Core.Tests;

/// <summary>split semantics (mirrors Camel's Splitter* tests).</summary>
public class SplitTests : ContextTestBase
{
    [Fact]
    public async Task Splits_a_list_and_sets_the_split_properties()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Split(Expr.Body(), s => s.To("mock:split"))
            .To("mock:result"));
        var split = Mock("split");
        split.ExpectedBodiesReceived("A", "B", "C");
        var body = new List<string> { "A", "B", "C" };
        Mock("result").ExpectedBodiesReceived(body);

        var sent = await Template.SendAsync("direct:start", e => e.Message.Body = body);

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        for (var i = 0; i < 3; i++)
        {
            var part = split.ReceivedExchanges[i];
            Assert.Equal(i, part.GetProperty(ExchangePropertyKeys.SplitIndex));
            Assert.Equal(3, part.GetProperty(ExchangePropertyKeys.SplitSize));
            Assert.Equal(i == 2, part.GetProperty(ExchangePropertyKeys.SplitComplete));
            Assert.Equal(sent.ExchangeId, part.GetProperty(ExchangePropertyKeys.CorrelationId));
            Assert.Null(part.GetProperty(ExchangePropertyKeys.MulticastIndex));
        }

        Assert.Null(sent.GetProperty(ExchangePropertyKeys.SplitIndex));
    }

    [Fact]
    public async Task Without_a_strategy_the_original_message_continues_unchanged()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Split(Expr.Body(), s => s.Transform(_ => "changed").SetHeader("h", Expr.Constant("part")))
            .To("mock:result"));
        Mock("result").ExpectedBodiesReceived("only");
        Mock("result").ExpectedHeaderReceived("h", "orig");

        await Template.SendBodyAndHeaderAsync("direct:start", "only", "h", "orig");

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Splits_arrays_lazy_enumerables_and_value_types()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:array").Split(Expr.Body(), s => s.To("mock:array"));
            b.From("direct:lazy").Split(_ => Enumerable.Range(1, 3).Select(i => i * 10), s => s.To("mock:lazy"));
        });
        Mock("array").ExpectedBodiesReceived(1, 2);
        Mock("lazy").ExpectedBodiesReceived(10, 20, 30);

        await Template.SendBodyAsync("direct:array", new[] { 1, 2 });
        await Template.SendBodyAsync("direct:lazy", null);

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        Assert.Equal(3, Mock("lazy").ReceivedExchanges[2].GetProperty(ExchangePropertyKeys.SplitSize));
    }

    [Fact]
    public async Task Splits_async_enumerables_of_reference_and_value_types()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:strings").Split(_ => Async("a", "b"), s => s.To("mock:strings"));
            b.From("direct:ints").Split(_ => Async(1, 2, 3), s => s.To("mock:ints"), streaming: true);
        });
        Mock("strings").ExpectedBodiesReceived("a", "b");
        Mock("ints").ExpectedBodiesReceived(1, 2, 3);

        await Template.SendBodyAsync("direct:strings", null);
        await Template.SendBodyAsync("direct:ints", null);

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Typed_overload_splits_a_value_computed_from_the_body()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Split((Basket basket) => basket.Items, s => s.To("mock:item")));
        Mock("item").ExpectedBodiesReceived("apple", "pear");

        await Template.SendBodyAsync("direct:start", new Basket(["apple", "pear"]));

        await Mock("item").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Strings_are_not_split_unless_a_delimiter_or_tokenizer_is_used()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:plain").Split(Expr.Body(), s => s.To("mock:plain"));
            b.From("direct:delimiter").Split(Expr.Body(), s => s.To("mock:delimiter"), delimiter: ",");
            b.From("direct:tokenize").Split(Expr.Body().Tokenize(","), s => s.To("mock:tokenize"));
        });
        Mock("plain").ExpectedBodiesReceived("A, B,C");
        Mock("delimiter").ExpectedBodiesReceived("A", "B", "C");
        Mock("tokenize").ExpectedBodiesReceived("A", " B", "C");

        await Template.SendBodyAsync("direct:plain", "A, B,C");
        await Template.SendBodyAsync("direct:delimiter", "A, B,,C");
        await Template.SendBodyAsync("direct:tokenize", "A, B,C");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Tokenizer_supports_regex_grouping_and_skip_first()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:regex").Split(Expr.Tokenize(@"\s*[,;]\s*", regex: true), s => s.To("mock:regex"));
            b.From("direct:group").Split(Expr.Tokenize("\n", group: 2, skipFirst: true), s => s.To("mock:group"));
        });
        Mock("regex").ExpectedBodiesReceived("a", "b", "c");
        Mock("group").ExpectedBodiesReceived("1\n2", "3\n4", "5");

        await Template.SendBodyAsync("direct:regex", "a , b;c");
        await Template.SendBodyAsync("direct:group", "header\n1\n2\n3\n4\n5\n");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Streaming_split_of_stream_lines_sets_the_size_only_on_the_last_part()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Split(Expr.Lines(), s => s.To("mock:line"), streaming: true)
            .To("mock:result"));
        var line = Mock("line");
        line.ExpectedBodiesReceived("one", "two", "three");

        await Template.SendBodyAsync("direct:start", new MemoryStream(Encoding.UTF8.GetBytes("one\r\ntwo\nthree\n")));

        await line.AssertIsSatisfiedAsync();
        Assert.Null(line.ReceivedExchanges[0].GetProperty(ExchangePropertyKeys.SplitSize));
        Assert.Null(line.ReceivedExchanges[1].GetProperty(ExchangePropertyKeys.SplitSize));
        Assert.Equal(3, line.ReceivedExchanges[2].GetProperty(ExchangePropertyKeys.SplitSize));
        Assert.Equal([false, false, true], line.ReceivedExchanges.Select(e => e.GetProperty<bool>(ExchangePropertyKeys.SplitComplete)));
    }

    [Fact]
    public async Task Streaming_split_reads_parts_on_demand()
    {
        var produced = 0;
        IEnumerable<int> Source()
        {
            for (var i = 0; i < 4; i++)
            {
                produced++;
                yield return i;
            }
        }

        var seenWhenProcessed = new List<int>();
        await StartWithRoutes(b => b.From("direct:start")
            .Split(_ => Source(), s => s.Process(_ => seenWhenProcessed.Add(produced)), streaming: true));

        await Template.SendBodyAsync("direct:start", null);

        // One part of look-ahead (to know whether the current part is the last one), never the whole sequence.
        Assert.Equal([2, 3, 4, 4], seenWhenProcessed);
    }

    [Fact]
    public async Task Non_streaming_split_reads_all_parts_first()
    {
        var produced = 0;
        IEnumerable<int> Source()
        {
            for (var i = 0; i < 3; i++)
            {
                produced++;
                yield return i;
            }
        }

        var seenWhenProcessed = new List<int>();
        await StartWithRoutes(b => b.From("direct:start").Split(_ => Source(), s => s.Process(_ => seenWhenProcessed.Add(produced))));

        await Template.SendBodyAsync("direct:start", null);

        Assert.Equal([3, 3, 3], seenWhenProcessed);
    }

    [Fact]
    public async Task Null_and_empty_values_split_into_nothing()
    {
        await StartWithRoutes(b => b.From("direct:start").Split(Expr.Header("parts"), s => s.To("mock:part")).To("mock:result"));
        ExpectNone("part");
        Mock("result").ExpectedBodiesReceived("x", "y");

        await Template.SendBodyAsync("direct:start", "x");
        await Template.SendBodyAndHeaderAsync("direct:start", "y", "parts", Array.Empty<string>());

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Message_parts_replace_the_message()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Split(
                e =>
                {
                    var first = new Message(e) { Body = "m1" };
                    first.SetHeader("n", 1);
                    var second = new Message(e) { Body = "m2" };
                    second.SetHeader("n", 2);
                    return new[] { first, second };
                },
                s => s.To("mock:part")));
        Mock("part").ExpectedBodiesReceived("m1", "m2");
        Mock("part").ExpectedHeaderValuesReceivedInAnyOrder("n", 1, 2);

        await Template.SendBodyAsync("direct:start", null);

        await Mock("part").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Grouped_body_strategy_collects_the_part_replies()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Split(Expr.Tokenize(","), s => s.Transform((string p) => p.ToUpperInvariant()), aggregationStrategy: AggregationStrategies.GroupedBody())
            .To("mock:result"));

        var reply = await Template.RequestBodyAsync("direct:start", "a,b,c");

        Assert.Equal(new object?[] { "A", "B", "C" }, Assert.IsType<List<object?>>(reply));
    }

    [Fact]
    public async Task Typed_body_strategy_sums_the_parts()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Split(Expr.Body(), s => s.Transform((int n) => n * 2), aggregationStrategy: AggregationStrategies.Body((int sum, int n) => sum + n)));

        Assert.Equal(12, await Template.RequestBodyAsync<int>("direct:start", new[] { 1, 2, 3 }));
    }

    [Fact]
    public async Task Failed_part_fails_the_exchange_after_all_parts_by_default()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Split(Expr.Tokenize(","), s => s
                .Filter((string p) => p == "Kaboom", f => f.ThrowException(new InvalidOperationException("Forced")))
                .To("mock:line"))
            .To("mock:result"));
        Mock("line").ExpectedBodiesReceived("Hello", "World");
        ExpectNone("result");

        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", "Hello,Kaboom,World"));

        Assert.Equal("Forced", ex.InnerException!.Message);
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Use_original_without_exception_propagation_ignores_failed_parts()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Split(
                Expr.Tokenize(","),
                s => s.Filter((string p) => p == "Kaboom", f => f.ThrowException(new InvalidOperationException("Forced"))).To("mock:line"),
                aggregationStrategy: AggregationStrategies.UseOriginal(propagateException: false))
            .To("mock:result"));
        Mock("line").ExpectedBodiesReceived("Hello", "World");
        Mock("result").ExpectedBodiesReceived("Hello,Kaboom,World");

        await Template.SendBodyAsync("direct:start", "Hello,Kaboom,World");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Stop_on_exception_stops_splitting()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Split(
                Expr.Tokenize(","),
                s => s.Filter((string p) => p.StartsWith("Kaboom", StringComparison.Ordinal), f => f.ThrowException(new InvalidOperationException("Forced"))).To("mock:split"),
                stopOnException: true)
            .To("mock:result"));
        Mock("split").ExpectedBodiesReceived("Hello World");
        ExpectNone("result");

        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", "Hello World,Kaboom World,Bye World"));

        var cause = Assert.IsType<CaravanExchangeException>(ex.InnerException);
        Assert.StartsWith("Multicast processing failed for number 1", cause.Message, StringComparison.Ordinal);
        Assert.Equal("Forced", cause.InnerException!.Message);
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Stop_on_exception_lets_a_successful_split_through()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Split(Expr.Tokenize(","), s => s.To("mock:split"), stopOnException: true)
            .To("mock:result"));
        Mock("split").ExpectedBodiesReceived("Hello World", "Bye World");
        Mock("result").ExpectedBodiesReceived("Hello World,Bye World");

        await Template.SendBodyAsync("direct:start", "Hello World,Bye World");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Parallel_split_runs_parts_concurrently_and_aggregates_in_order()
    {
        var probe = new ConcurrencyProbe();
        await StartWithRoutes(b => b.From("direct:start")
            .Split(
                Expr.Body(),
                s => s.Process(async _ => await probe.EnterAsync()).Transform((string p) => p + "!"),
                parallelProcessing: true,
                maxDegreeOfParallelism: 4,
                aggregationStrategy: AggregationStrategies.String(" ")));

        var reply = Template.RequestBodyAsync<string>("direct:start", new[] { "a", "b", "c", "d", "e", "f" });
        await probe.WaitForStartedAsync(4);
        probe.Release(2);
        await probe.WaitForStartedAsync(6);
        probe.Release(4);

        Assert.Equal("a! b! c! d! e! f!", await reply.Bounded());
        Assert.Equal(4, probe.Max);
    }

    [Fact]
    public async Task Parallel_streaming_split_of_an_async_sequence_with_unknown_size()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Split(_ => Async(1, 2, 3, 4), s => s.To("mock:part"), parallelProcessing: true, streaming: true, aggregationStrategy: AggregationStrategies.Body((int sum, int n) => sum + n)));
        Mock("part").ExpectedBodiesReceivedInAnyOrder(1, 2, 3, 4);

        Assert.Equal(10, await Template.RequestBodyAsync<int>("direct:start", null));

        await Mock("part").AssertIsSatisfiedAsync();
        var last = Mock("part").ReceivedExchanges.Single(e => e.GetProperty<bool>(ExchangePropertyKeys.SplitComplete));
        Assert.Equal(3, last.GetProperty(ExchangePropertyKeys.SplitIndex));
        Assert.Equal(4, last.GetProperty(ExchangePropertyKeys.SplitSize));
        Assert.Equal(1, Mock("part").ReceivedExchanges.Count(e => e.GetProperty(ExchangePropertyKeys.SplitSize) is not null));
    }

    [Fact]
    public async Task Parallel_split_with_a_failure_fails_the_exchange_after_all_parts()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Split(
                Expr.Body(),
                s => s.Filter((int n) => n == 2, f => f.ThrowException(new InvalidOperationException("two"))).To("mock:part"),
                parallelProcessing: true)
            .To("mock:result"));
        Mock("part").ExpectedBodiesReceivedInAnyOrder(1, 3, 4);
        ExpectNone("result");

        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", new[] { 1, 2, 3, 4 }));

        Assert.Equal("two", ex.InnerException!.Message);
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Nested_split()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Split(
                Expr.Tokenize(";"),
                outer => outer
                    .SetHeader("group", e => e.GetProperty(ExchangePropertyKeys.SplitIndex))
                    .Split(Expr.Tokenize(","), inner => inner.To("mock:inner"))
                    .To("mock:outer"))
            .To("mock:result"));
        Mock("inner").ExpectedBodiesReceived("a", "b", "c", "d", "e");
        Mock("inner").ExpectedHeaderValuesReceivedInAnyOrder("group", 0, 0, 1, 1, 1);
        Mock("outer").ExpectedBodiesReceived("a,b", "c,d,e");
        Mock("result").ExpectedBodiesReceived("a,b;c,d,e");

        await Template.SendBodyAsync("direct:start", "a,b;c,d,e");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        Assert.Equal(2, Mock("inner").ReceivedExchanges[4].GetProperty(ExchangePropertyKeys.SplitIndex));
    }

    [Fact]
    public async Task Failure_inside_split_is_caught_by_do_try()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .DoTry(
                t => t.Split(Expr.Tokenize(","), s => s.Filter((string p) => p == "bad", f => f.ThrowException(new InvalidOperationException("bad part")))),
                c => c.DoCatch<InvalidOperationException>(r => r.SetBody(Expr.ExceptionMessage()).To("mock:caught")))
            .To("mock:result"));
        Mock("caught").ExpectedBodiesReceived("bad part");
        Mock("result").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "good,bad,good");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Do_try_inside_the_split_block_handles_failures_per_part()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Split(
                Expr.Tokenize(","),
                s => s.DoTry(
                    t => t.Filter((string p) => p == "bad", f => f.ThrowException(new InvalidOperationException("bad part"))).To("mock:ok"),
                    c => c.DoCatch<InvalidOperationException>(r => r.To("mock:caught"))))
            .To("mock:result"));
        Mock("ok").ExpectedBodiesReceived("a", "c");
        Mock("caught").ExpectedBodiesReceived("bad");
        Mock("result").ExpectedBodiesReceived("a,bad,c");

        await Template.SendBodyAsync("direct:start", "a,bad,c");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Dead_letter_channel_handles_failed_parts_and_the_split_continues()
    {
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead"));
            b.From("direct:start")
                .Split(Expr.Tokenize(","), s => s.Filter((string p) => p == "bad", f => f.ThrowException(new InvalidOperationException("bad part"))).To("mock:line"))
                .To("mock:result");
        });
        Mock("dead").ExpectedBodiesReceived("bad");
        Mock("line").ExpectedBodiesReceived("a", "c");
        Mock("result").ExpectedBodiesReceived("a,bad,c");

        await Template.SendBodyAsync("direct:start", "a,bad,c");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task On_exception_handled_in_the_route_applies_to_each_part()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .OnException<InvalidOperationException>(o => o.Handled(true).To("mock:handled"))
            .Split(Expr.Tokenize(","), s => s.Filter((string p) => p == "bad", f => f.ThrowException(new InvalidOperationException("bad part"))).To("mock:line"))
            .To("mock:result"));
        Mock("handled").ExpectedBodiesReceived("bad");
        Mock("line").ExpectedBodiesReceived("a", "c");
        Mock("result").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "a,bad,c");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Share_unit_of_work_hands_the_failure_to_the_parent_error_handler_with_the_original_message()
    {
        // Camel's SplitSubUnitOfWorkTest: the part is redelivered 3 times, then the whole original message goes to the dead letter channel.
        var attempts = 0;
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead").UseOriginalMessage().MaximumRedeliveries(3).RedeliveryDelay(0));
            b.From("direct:start")
                .To("mock:a")
                .Split(Expr.Tokenize(","), s => s.To("mock:b").To("direct:line"), shareUnitOfWork: true)
                .To("mock:result");
            b.From("direct:line")
                .Process(e =>
                {
                    if (e.Message.GetBody<string>() == "Donkey")
                    {
                        attempts++;
                        throw new InvalidOperationException("Donkey is not allowed");
                    }
                })
                .To("mock:line");
        });
        Mock("dead").ExpectedBodiesReceived("Tiger,Donkey,Camel");
        Mock("a").ExpectedBodiesReceived("Tiger,Donkey,Camel");
        Mock("b").ExpectedBodiesReceived("Tiger", "Donkey", "Camel");
        Mock("line").ExpectedBodiesReceived("Tiger", "Camel");
        ExpectNone("result");

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "Tiger,Donkey,Camel");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        Assert.Equal(4, attempts);
        Assert.Null(exchange.Exception);
        Assert.True(exchange.ErrorHandlerHandled);
    }

    [Fact]
    public async Task Share_unit_of_work_succeeds_like_a_normal_split()
    {
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead").UseOriginalMessage());
            b.From("direct:start").Split(Expr.Tokenize(","), s => s.To("mock:b"), shareUnitOfWork: true).To("mock:result");
        });
        ExpectNone("dead");
        Mock("b").ExpectedBodiesReceived("Tiger", "Camel");
        Mock("result").ExpectedBodiesReceived("Tiger,Camel");

        await Template.SendBodyAsync("direct:start", "Tiger,Camel");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Without_shared_unit_of_work_the_dead_letter_channel_gets_the_part()
    {
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead").UseOriginalMessage().MaximumRedeliveries(1).RedeliveryDelay(0));
            b.From("direct:start")
                .Split(Expr.Tokenize(","), s => s.Filter((string p) => p == "Donkey", f => f.ThrowException(new InvalidOperationException("no"))))
                .To("mock:result");
        });
        Mock("dead").ExpectedBodiesReceived("Donkey");
        Mock("result").ExpectedBodiesReceived("Tiger,Donkey,Camel");

        await Template.SendBodyAsync("direct:start", "Tiger,Donkey,Camel");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task On_prepare_can_deep_copy_mutable_parts()
    {
        var shared = new List<string>();
        await StartWithRoutes(b => b.From("direct:start")
            .Split(
                _ => new[] { shared, shared },
                s => s.Process(e => e.Message.GetBody<List<string>>()!.Add("x")),
                onPrepare: e => e.Message.Body = new List<string>(e.Message.GetBody<List<string>>()!)));

        await Template.SendBodyAsync("direct:start", null);

        Assert.Empty(shared);
    }

    [Fact]
    public async Task Failing_split_expression_fails_the_exchange()
    {
        await StartWithRoutes(b => b.From("direct:start").Split(_ => throw new InvalidOperationException("no parts"), s => s.To("mock:part")));
        ExpectNone("part");

        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", "x"));

        Assert.Equal("no parts", ex.InnerException!.Message);
    }

    [Fact]
    public async Task Failure_while_streaming_parts_fails_the_exchange()
    {
        static IEnumerable<string> Broken()
        {
            yield return "a";
            throw new IOException("read error");
        }

        await StartWithRoutes(b => b.From("direct:start").Split(_ => Broken(), s => s.To("mock:part"), streaming: true));

        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", "x"));

        Assert.IsType<IOException>(ex.InnerException);
    }

    [Fact]
    public async Task Cancellation_stops_a_streaming_split()
    {
        using var cts = new CancellationTokenSource();
        await StartWithRoutes(b => b.From("direct:start")
            .Split(Expr.Body(), s => s.Process(e =>
            {
                if (e.GetProperty<int>(ExchangePropertyKeys.SplitIndex) == 1)
                {
                    cts.Cancel();
                }
            }).To("mock:part"), streaming: true));

        var exchange = await Template.SendAsync("direct:start", e =>
        {
            e.Message.Body = new[] { 1, 2, 3, 4 };
            e.CancellationToken = cts.Token;
        });

        Assert.IsAssignableFrom<OperationCanceledException>(exchange.Exception);
        Assert.Equal(2, Mock("part").ReceivedCounter);
    }

    [Fact]
    public async Task Parallel_split_with_cancellation_waits_for_running_parts_and_fails()
    {
        using var cts = new CancellationTokenSource();
        var probe = new ConcurrencyProbe();
        await StartWithRoutes(b => b.From("direct:start")
            .Split(Expr.Body(), s => s.Process(_ => probe.EnterAsync()).To("mock:part"), parallelProcessing: true, maxDegreeOfParallelism: 2));

        var send = Template.SendAsync("direct:start", e =>
        {
            e.Message.Body = new[] { 1, 2, 3, 4, 5 };
            e.CancellationToken = cts.Token;
        });
        await probe.WaitForStartedAsync(2);
        cts.Cancel();
        probe.Release(2);
        var exchange = await send.Bounded();

        Assert.IsAssignableFrom<OperationCanceledException>(exchange.Exception);
        Assert.Equal(2, probe.StartedCount);
        Assert.Equal(2, Mock("part").ReceivedCounter);
    }

    private static async IAsyncEnumerable<T> Async<T>(params T[] items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    private sealed record Basket(List<string> Items);
}
