using System.Diagnostics;
using Caravan.Builder;
using Caravan.Observability;

namespace Caravan.Core.Tests.Observability;

public class TracingTests : ContextTestBase
{
    private readonly ActivityCollector _spans;

    public TracingTests()
    {
        Context.Name = "tracing-" + Guid.NewGuid().ToString("N");
        _spans = new ActivityCollector(Context.Name);
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        _spans.Dispose();
    }

    [Fact]
    public async Task Direct_routes_produce_a_parent_child_chain_of_send_and_route_spans()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:a").RouteId("a").To("direct:b");
            b.From("direct:b").RouteId("b").To("mock:end");
        });

        await Template.RequestBodyAsync("direct:a", "hi");

        var sendA = _spans.Single("send direct:a");
        var routeA = _spans.Single("route a");
        var sendB = _spans.Single("send direct:b");
        var routeB = _spans.Single("route b");
        var sendMock = _spans.Single("send mock:end");

        Assert.Equal(ActivityKind.Client, sendA.Kind);
        Assert.Null(sendA.Parent);
        Assert.Equal(sendA.SpanId, routeA.ParentSpanId);
        Assert.Equal(ActivityKind.Server, routeA.Kind); // InOut entering the context
        Assert.Equal(routeA.SpanId, sendB.ParentSpanId);
        Assert.Equal(sendB.SpanId, routeB.ParentSpanId);
        Assert.Equal(ActivityKind.Internal, routeB.Kind); // direct call within the same exchange
        Assert.Equal(routeB.SpanId, sendMock.ParentSpanId);
        Assert.All(_spans.Spans, s => Assert.Equal(sendA.TraceId, s.TraceId));

        Assert.Equal("a", routeA.GetTagItem(CaravanSemanticConventions.RouteId));
        Assert.Equal("direct:a", routeA.GetTagItem(CaravanSemanticConventions.EndpointUri));
        Assert.Equal("direct", routeA.GetTagItem(CaravanSemanticConventions.MessagingSystem));
        Assert.Equal("a", routeA.GetTagItem(CaravanSemanticConventions.MessagingDestinationName));
        Assert.Equal("process", routeA.GetTagItem(CaravanSemanticConventions.MessagingOperationType));
        Assert.NotNull(routeA.GetTagItem(CaravanSemanticConventions.ExchangeId));
        Assert.Equal("send", sendMock.GetTagItem(CaravanSemanticConventions.MessagingOperationType));
        Assert.Equal("b", sendMock.GetTagItem(CaravanSemanticConventions.RouteId));
        Assert.All(_spans.Spans, s => Assert.Equal(ActivityStatusCode.Unset, s.Status));
    }

    [Fact]
    public async Task Trace_context_is_propagated_across_seda_through_headers()
    {
        var mock = Mock("mock:end").ExpectedMessageCount(1);
        await StartWithRoutes(b =>
        {
            b.From("direct:in").RouteId("in").To("seda:q");
            b.From("seda:q").RouteId("consumer").To("mock:end");
        });

        await Template.SendBodyAsync("direct:in", "hi");
        await mock.AssertIsSatisfiedAsync();
        await TestHelpers.WaitUntilAsync(() => _spans.Spans.Any(s => s.DisplayName == "route consumer"));

        var send = _spans.Single("send seda:q");
        var consumer = _spans.Single("route consumer");
        Assert.Equal(ActivityKind.Producer, send.Kind);
        Assert.Equal(ActivityKind.Consumer, consumer.Kind);
        Assert.Equal(send.TraceId, consumer.TraceId);
        Assert.Equal(send.SpanId, consumer.ParentSpanId);
        Assert.True(consumer.HasRemoteParent);

        var received = mock.GetExchanges()[0].Message;
        Assert.Equal(_spans.Single("send mock:end").Id, received.GetHeader(CaravanSemanticConventions.TraceParentHeader));
    }

    [Fact]
    public async Task An_incoming_traceparent_header_is_the_parent_of_the_route_span()
    {
        await StartWithRoutes(b => b.From("direct:in").RouteId("in").To("mock:end"));
        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        Context.Telemetry.PropagateTraceContext = true;

        // Sent straight to the route's consumer (no producer span) so the header is the only parent.
        var exchange = new Exchange(Context);
        exchange.Message.SetHeader("traceparent", $"00-{traceId}-{spanId}-01");
        exchange.Message.SetHeader("tracestate", "vendor=1");
        exchange.Message.SetHeader("baggage", "tenant=acme");
        await Context.GetRoute("in")!.Processor.ProcessAsync(exchange);

        var route = _spans.Single("route in");
        Assert.Equal(traceId, route.TraceId);
        Assert.Equal(spanId, route.ParentSpanId);
        Assert.Equal("vendor=1", route.TraceStateString);
        Assert.Equal("acme", route.GetBaggageItem("tenant"));
        Assert.Equal(route.SpanId, _spans.Single("send mock:end").ParentSpanId);
    }

    [Fact]
    public async Task Failures_set_the_error_status_with_an_exception_event()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:a").RouteId("a").To("direct:boom");
            b.From("direct:boom").RouteId("boom").Process(_ => throw new InvalidOperationException("kaboom"));
        });

        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:a", "x"));

        foreach (var name in new[] { "route a", "route boom", "send direct:boom", "send direct:a" })
        {
            var span = _spans.Single(name);
            Assert.Equal(ActivityStatusCode.Error, span.Status);
            Assert.Equal("kaboom", span.StatusDescription);
            var evt = Assert.Single(span.Events, e => e.Name == "exception");
            Assert.Contains(evt.Tags, t => t.Key == "exception.type" && Equals(t.Value, typeof(InvalidOperationException).FullName));
        }
    }

    [Fact]
    public async Task Handled_failures_do_not_mark_the_span_as_failed()
    {
        await StartWithRoutes(b =>
        {
            b.OnException<InvalidOperationException>(o => o.Handled(true));
            b.From("direct:a").RouteId("a").Process(_ => throw new InvalidOperationException("handled"));
        });

        await Template.SendBodyAsync("direct:a", "x");

        var route = _spans.Single("route a");
        Assert.Equal(ActivityStatusCode.Unset, route.Status);
        Assert.Equal(true, route.GetTagItem("caravan.exchange.failure_handled"));
    }

    [Fact]
    public async Task Endpoint_uris_in_spans_have_their_secrets_masked()
    {
        await StartWithRoutes(b => b.From("direct:a").RouteId("a").To("stub:remote?password=s3cret&user=bob&accessKey=AK"));

        await Template.SendBodyAsync("direct:a", "x");

        var send = Assert.Single(_spans.Spans, s => s.DisplayName.StartsWith("send stub:", StringComparison.Ordinal));
        var uri = (string)send.GetTagItem(CaravanSemanticConventions.EndpointUri)!;
        Assert.DoesNotContain("s3cret", uri, StringComparison.Ordinal);
        Assert.DoesNotContain("AK", uri, StringComparison.Ordinal);
        Assert.Contains("password=xxxxxx", uri, StringComparison.Ordinal);
        Assert.Contains("user=bob", uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Node_spans_are_children_of_the_route_span_when_enabled()
    {
        Context.Telemetry.TraceProcessors = true;
        await StartWithRoutes(b => b.From("direct:a").RouteId("a")
            .SetHeader("x", Expr.Constant(1)).Id("setX")
            .Filter(Pred.Constant(true), f => f.To("mock:in").Id("toIn")).Id("filter1"));

        await Template.SendBodyAsync("direct:a", "x");

        var route = _spans.Single("route a");
        var setHeader = Assert.Single(_spans.Spans, s => (string?)s.GetTagItem(CaravanSemanticConventions.NodeId) == "setX");
        var filter = Assert.Single(_spans.Spans, s => (string?)s.GetTagItem(CaravanSemanticConventions.NodeId) == "filter1");
        var toIn = Assert.Single(_spans.Spans, s => (string?)s.GetTagItem(CaravanSemanticConventions.NodeId) == "toIn");
        var send = _spans.Single("send mock:in");
        Assert.Equal(route.SpanId, setHeader.ParentSpanId);
        Assert.Equal(route.SpanId, filter.ParentSpanId);
        Assert.Equal(filter.SpanId, toIn.ParentSpanId);
        Assert.Equal(toIn.SpanId, send.ParentSpanId);
        Assert.Equal("to[mock:in]", toIn.GetTagItem(CaravanSemanticConventions.NodeLabel));
    }

    [Fact]
    public async Task Node_spans_are_off_by_default()
    {
        await StartWithRoutes(b => b.From("direct:a").RouteId("a").SetBody(Expr.Constant(1)).To("mock:a"));

        await Template.SendBodyAsync("direct:a", "x");

        Assert.DoesNotContain(_spans.Spans, s => s.GetTagItem(CaravanSemanticConventions.NodeId) is not null);
        Assert.Equal(3, _spans.Spans.Count);
    }

    [Fact]
    public async Task Disabled_tracing_creates_no_spans_and_injects_no_headers()
    {
        Context.Telemetry.TracingEnabled = false;
        var mock = Mock("mock:a").ExpectedMessageCount(1);
        await StartWithRoutes(b => b.From("direct:a").RouteId("a").To("mock:a"));

        await Template.SendBodyAsync("direct:a", "x");

        Assert.Empty(_spans.Spans);
        Assert.Null(mock.GetExchanges()[0].Message.GetHeader("traceparent"));
    }

    [Fact]
    public async Task An_ambient_activity_is_propagated_even_without_caravan_spans()
    {
        Context.Telemetry.TracingEnabled = false;
        var mock = Mock("mock:a").ExpectedMessageCount(1);
        await StartWithRoutes(b => b.From("direct:a").RouteId("a").To("mock:a"));

        using var ambient = new Activity("app").SetIdFormat(ActivityIdFormat.W3C).Start();
        await Template.SendBodyAsync("direct:a", "x");
        ambient.Stop();

        Assert.Equal(ambient.Id, mock.GetExchanges()[0].Message.GetHeader("traceparent"));
    }
}
