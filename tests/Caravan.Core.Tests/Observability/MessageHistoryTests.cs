using Caravan.Builder;
using Caravan.Observability;
using Microsoft.Extensions.Logging;

namespace Caravan.Core.Tests.Observability;

public class MessageHistoryTests : ContextTestBase
{
    [Fact]
    public async Task Each_route_input_and_node_is_recorded_in_order()
    {
        Context.MessageHistory = true;
        var mock = Mock("mock:end").ExpectedMessageCount(1);
        await StartWithRoutes(b =>
        {
            b.From("direct:a").RouteId("a").SetHeader("x", Expr.Constant(1)).Id("set").To("direct:b").Id("callB");
            b.From("direct:b").RouteId("b").Choice(c => c.When(Pred.Constant(true), w => w.To("mock:end").Id("toEnd"))).Id("choice");
        });

        await Template.SendBodyAsync("direct:a", "x");

        var history = MessageHistoryFormatter.GetHistory(mock.GetExchanges()[0]);
        Assert.Equal(
            ["a/a from[direct:a]", "a/set setHeader[x]", "a/callB to[direct:b]", "b/b from[direct:b]", "b/choice choice", "b/toEnd to[mock:end]"],
            history.Select(h => $"{h.RouteId}/{h.NodeId} {h.Label}"));
    }

    [Fact]
    public async Task Elapsed_times_are_recorded_when_steps_finish()
    {
        Context.MessageHistory = true;
        await StartWithRoutes(b => b.From("direct:a").RouteId("a").SetBody(Expr.Constant("y")).Id("set"));

        var exchange = await Template.SendAsync("direct:a", e => e.Message.Body = "x");

        var history = MessageHistoryFormatter.GetHistory(exchange);
        Assert.Equal(2, history.Count);
        Assert.All(history, h => Assert.NotNull(h.Elapsed));
    }

    [Fact]
    public async Task Disabled_by_default()
    {
        var mock = Mock("mock:end").ExpectedMessageCount(1);
        await StartWithRoutes(b => b.From("direct:a").To("mock:end"));

        await Template.SendBodyAsync("direct:a", "x");

        Assert.Null(mock.GetExchanges()[0].GetProperty(ExchangePropertyKeys.MessageHistory));
    }

    [Fact]
    public async Task Split_parts_get_their_own_history()
    {
        Context.MessageHistory = true;
        var mock = Mock("mock:part").ExpectedMessageCount(2);
        await StartWithRoutes(b => b.From("direct:a").RouteId("a").Split(Expr.Body(), s => s.To("mock:part").Id("toPart")).Id("split"));

        await Template.SendBodyAsync("direct:a", new[] { "1", "2" });

        foreach (var part in mock.GetExchanges())
        {
            var history = MessageHistoryFormatter.GetHistory(part);
            Assert.Equal(["a", "split", "toPart"], history.Select(h => h.NodeId));
        }
    }

    [Fact]
    public async Task Simple_messageHistory_renders_the_table()
    {
        Context.MessageHistory = true;
        await StartWithRoutes(b => b.From("direct:a").RouteId("a").SetBody(Lang.Simple("${messageHistory(false)}")).Id("render"));

        var text = await Template.RequestBodyAsync<string>("direct:a", "x");

        Assert.Contains("Message History", text, StringComparison.Ordinal);
        Assert.Contains("[a]", text, StringComparison.Ordinal);
        Assert.Contains("[from[direct:a]]", text, StringComparison.Ordinal);
        Assert.Contains("[render]", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Exchange[", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Simple_messageHistory_says_when_it_is_disabled()
    {
        await StartWithRoutes(b => b.From("direct:a").SetBody(Lang.Simple("${messageHistory}")));

        var text = await Template.RequestBodyAsync<string>("direct:a", "x");

        Assert.Contains("message history is disabled", text, StringComparison.Ordinal);
        Assert.Contains("Exchange[", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_default_error_handler_logs_the_history_table_when_exhausted()
    {
        var logs = new CapturingLoggerProvider();
        Context.LoggerFactory = LoggerFactory.Create(l => l.AddProvider(logs));
        Context.MessageHistory = true;
        await StartWithRoutes(b =>
        {
            b.From("direct:a").RouteId("a").To("direct:b").Id("callB");
            b.From("direct:b").RouteId("b").Process(_ => throw new InvalidOperationException("boom")).Id("fail");
        });

        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:a", "payload"));

        var log = Assert.Single(logs.Messages, m => m.Level == LogLevel.Error && m.Message.Contains("Exhausted", StringComparison.Ordinal)).Message;
        Assert.Contains("Message History", log, StringComparison.Ordinal);
        var lines = log.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var rows = lines.Where(l => l.StartsWith('[')).ToList();
        Assert.Equal(4, rows.Count);
        Assert.StartsWith("[a]", rows[0], StringComparison.Ordinal);
        Assert.Contains("[from[direct:a]]", rows[0], StringComparison.Ordinal);
        Assert.Contains("[callB]", rows[1], StringComparison.Ordinal);
        Assert.Contains("[from[direct:b]]", rows[2], StringComparison.Ordinal);
        Assert.Contains("[fail]", rows[3], StringComparison.Ordinal);
        Assert.Contains("payload", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_history_table_can_be_left_out_of_the_exhausted_log()
    {
        var logs = new CapturingLoggerProvider();
        Context.LoggerFactory = LoggerFactory.Create(l => l.AddProvider(logs));
        Context.MessageHistory = true;
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DefaultErrorHandler().LogExhaustedMessageHistory(false));
            b.From("direct:a").Process(_ => throw new InvalidOperationException("boom"));
        });

        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:a", "payload"));

        var log = Assert.Single(logs.Messages, m => m.Level == LogLevel.Error).Message;
        Assert.DoesNotContain("Message History", log, StringComparison.Ordinal);
    }
}
