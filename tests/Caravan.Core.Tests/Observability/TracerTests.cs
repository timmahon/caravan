using Caravan.Builder;
using Caravan.Observability;
using Microsoft.Extensions.Logging;

namespace Caravan.Core.Tests.Observability;

public class TracerTests : ContextTestBase
{
    private readonly CapturingLoggerProvider _logs = new();

    public TracerTests() => Context.LoggerFactory = LoggerFactory.Create(l => l.AddProvider(_logs));

    private List<string> TraceLines()
    {
        lock (_logs.Messages)
        {
            return [.. _logs.Messages.Where(m => m.Category == CaravanTracer.DefaultLoggerName).Select(m => m.Message)];
        }
    }

    [Fact]
    public async Task Tracing_logs_route_input_each_node_and_route_output()
    {
        Context.Tracing = true;
        await StartWithRoutes(b => b.From("direct:a").RouteId("a").SetBody(Expr.Constant("changed")).Id("set").To("mock:a").Id("toA"));

        await Template.SendBodyAsync("direct:a", "hello");

        var lines = TraceLines();
        Assert.Equal(4, lines.Count);
        Assert.StartsWith("[a] *--> direct:a", lines[0], StringComparison.Ordinal);
        Assert.Contains("hello", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("[a] [set] setBody", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("[a] [toA] to[mock:a]", lines[2], StringComparison.Ordinal);
        Assert.Contains("changed", lines[2], StringComparison.Ordinal);
        Assert.StartsWith("[a] *<-- direct:a", lines[3], StringComparison.Ordinal);
        Assert.All(_logs.Messages.Where(m => m.Category == CaravanTracer.DefaultLoggerName), m => Assert.Equal(LogLevel.Information, m.Level));
    }

    [Fact]
    public async Task Tracing_is_off_by_default_and_standby_allows_turning_it_on_later()
    {
        Context.Tracer.Standby = true;
        await StartWithRoutes(b => b.From("direct:a").RouteId("a").To("mock:a"));

        await Template.SendBodyAsync("direct:a", "before");
        Assert.Empty(TraceLines());

        Context.Tracing = true;
        await Template.SendBodyAsync("direct:a", "after");
        Assert.Equal(3, TraceLines().Count);
    }

    [Fact]
    public async Task The_trace_pattern_selects_routes_or_nodes()
    {
        Context.Tracing = true;
        Context.Tracer.TracePattern = "b*";
        Context.Tracer.TraceBeforeAndAfterRoute = false;
        await StartWithRoutes(b =>
        {
            b.From("direct:a").RouteId("a").To("mock:a").Id("toA").To("direct:b").Id("callB");
            b.From("direct:b").RouteId("b").To("mock:b").Id("toB");
        });

        await Template.SendBodyAsync("direct:a", "x");

        var lines = TraceLines();
        Assert.Single(lines);
        Assert.StartsWith("[b] [toB]", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Backlog_tracer_keeps_the_last_steps_with_message_and_outcome()
    {
        Context.BacklogTracer.Enabled = true;
        await StartWithRoutes(b => b.From("direct:a").RouteId("a")
            .SetHeader("h", Expr.Constant("v")).Id("set")
            .Process(e =>
            {
                if (e.Message.GetBody<string>() == "bad")
                {
                    throw new InvalidOperationException("nope");
                }
            }).Id("check"));

        await Template.SendBodyAsync("direct:a", "good");
        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:a", "bad"));

        Assert.Equal(6, Context.BacklogTracer.TraceCounter);
        var check = Context.BacklogTracer.DumpTracedMessages("check");
        Assert.Equal(2, check.Count);
        Assert.Equal("good", check[0].Body);
        Assert.Equal("v", check[0].Headers["h"]);
        Assert.Equal(typeof(string).FullName, check[0].BodyType);
        Assert.Null(check[0].ExceptionAsText);
        Assert.NotNull(check[0].Elapsed);
        Assert.Contains("nope", check[1].ExceptionAsText, StringComparison.Ordinal);
        Assert.True(check[0].Uid < check[1].Uid);

        // Removed on dump: the route inputs and the setHeader steps remain.
        var rest = Context.BacklogTracer.DumpAllTracedMessages();
        Assert.Equal(4, rest.Count);
        Assert.Equal(2, rest.Count(e => e.IsRouteInput));
        Assert.Empty(Context.BacklogTracer.DumpAllTracedMessages());
    }

    [Fact]
    public async Task Backlog_tracer_honours_size_patterns_and_filter()
    {
        var backlog = Context.BacklogTracer;
        backlog.Enabled = true;
        backlog.BacklogSize = 3;
        backlog.RemoveOnDump = false;
        backlog.TraceRouteInput = false;
        backlog.ExcludePatterns = "skip*";
        backlog.TraceFilter = Pred.Simple("${body} != 'ignored'");
        await StartWithRoutes(b => b.From("direct:a").RouteId("a")
            .Log("x").Id("skipMe")
            .SetBody(Expr.Body()).Id("keep"));

        foreach (var body in new[] { "1", "ignored", "2", "3", "4" })
        {
            await Template.SendBodyAsync("direct:a", body);
        }

        var all = backlog.DumpAllTracedMessages();
        Assert.Equal(["2", "3", "4"], all.Select(e => e.Body));
        Assert.All(all, e => Assert.Equal("keep", e.NodeId));
        Assert.Equal(3, backlog.DumpAllTracedMessages().Count);
        Assert.Contains("keep", backlog.DumpTracedMessagesAsText("keep"), StringComparison.Ordinal);

        backlog.Clear();
        Assert.Equal(0, backlog.Count);
    }
}
