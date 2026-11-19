using Caravan.Builder;
using Caravan.Components.Direct;
using Caravan.Spi;

namespace Caravan.Core.Tests;

public class CoreRoutingTests : ContextTestBase
{
    [Fact]
    public async Task Routes_message_through_direct_to_mock()
    {
        await StartWithRoutes(b => b.From("direct:start").To("mock:result"));
        var mock = Mock("result");
        mock.ExpectedBodiesReceived("Hello", "World");

        await Template.SendBodyAsync("direct:start", "Hello");
        await Template.SendBodyAsync("direct:start", "World");

        await mock.AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Process_set_header_and_transform()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .SetHeader("greeting", Expr.Constant("hi"))
            .Process(e => e.Message.Body = e.Message.GetBody<string>() + "!")
            .Transform((string s) => s.ToUpperInvariant())
            .To("mock:result"));
        var mock = Mock("result");
        mock.ExpectedBodiesReceived("HELLO!");
        mock.ExpectedHeaderReceived("greeting", "hi");

        await Template.SendBodyAsync("direct:start", "hello");

        await mock.AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Request_reply_returns_transformed_body()
    {
        await StartWithRoutes(b => b.From("direct:start").SetBody(e => $"Bye {e.Message.Body}"));

        var reply = await Template.RequestBodyAsync<string>("direct:start", "World");

        Assert.Equal("Bye World", reply);
    }

    [Fact]
    public async Task Async_processor_is_awaited()
    {
        await StartWithRoutes(b => b.From("direct:start").Process(async e =>
        {
            await Task.Yield();
            e.Message.Body = 42;
        }));

        Assert.Equal(42, await Template.RequestBodyAsync<int>("direct:start", "x"));
    }

    [Fact]
    public async Task Routes_can_call_each_other()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:a").SetBody(e => e.Message.Body + "a").To("direct:b");
            b.From("direct:b").SetBody(e => e.Message.Body + "b");
        });

        Assert.Equal("xab", await Template.RequestBodyAsync<string>("direct:a", "x"));
    }

    [Fact]
    public async Task Exception_stops_routing_and_is_rethrown_by_request()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Process(_ => throw new InvalidOperationException("boom"))
            .To("mock:result"));
        var mock = Mock("result");
        mock.ExpectedMessageCount(0);

        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.RequestBodyAsync("direct:start", "x"));
        Assert.IsType<InvalidOperationException>(ex.InnerException);
        await mock.AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Send_exchange_returns_exception_on_exchange()
    {
        await StartWithRoutes(b => b.From("direct:start").Process(_ => throw new ArgumentException("bad")));

        var result = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.True(result.IsFailed);
        Assert.NotNull(result.GetException<ArgumentException>());
    }

    [Fact]
    public async Task Direct_without_consumer_fails_fast_when_not_blocking()
    {
        await Context.StartAsync();

        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:nobody?block=false", "x"));
        Assert.IsType<DirectConsumerNotAvailableException>(ex.InnerException);
    }

    [Fact]
    public async Task Direct_blocks_until_consumer_starts()
    {
        await StartWithRoutes(b => b.From("direct:late").AutoStartup(false).SetBody(_ => "done"));

        var request = Template.RequestBodyAsync<string>("direct:late?timeout=5000", "x");
        await Task.Delay(100);
        await Context.StartRouteAsync(Context.Routes[0].Id);

        Assert.Equal("done", await request);
    }

    [Fact]
    public async Task Direct_does_not_allow_two_consumers()
    {
        Context.AddRoutes(RouteBuilder.Create(b =>
        {
            b.From("direct:dup").To("mock:a");
            b.From("direct:dup").To("mock:b");
        }));

        await Assert.ThrowsAnyAsync<CaravanException>(() => Context.StartAsync());
    }

    [Fact]
    public async Task Stopped_route_does_not_consume_and_can_be_restarted()
    {
        await StartWithRoutes(b => b.From("direct:start").RouteId("r1").To("mock:result"));
        var mock = Mock("result");

        await Context.StopRouteAsync("r1");
        Assert.Equal(ServiceStatus.Stopped, Context.GetRoute("r1")!.Status);
        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start?block=false", "x"));

        await Context.StartRouteAsync("r1");
        mock.ExpectedBodiesReceived("y");
        await Template.SendBodyAsync("direct:start", "y");
        await mock.AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Routes_start_in_startup_order()
    {
        var started = new List<string>();
        Context.EventNotifiers.Add(new RecordingNotifier(e =>
        {
            if (e.Type == CaravanEventType.RouteStarted)
            {
                started.Add(e.RouteId!);
            }
        }));

        await StartWithRoutes(b =>
        {
            b.From("direct:a").RouteId("a").StartupOrder(3).To("mock:x");
            b.From("direct:b").RouteId("b").StartupOrder(1).To("mock:x");
            b.From("direct:c").RouteId("c").To("mock:x");
        });

        Assert.Equal(["b", "a", "c"], started);
    }

    [Fact]
    public async Task Duplicate_route_ids_are_rejected()
    {
        await Assert.ThrowsAsync<FailedToCreateRouteException>(() => StartWithRoutes(b =>
        {
            b.From("direct:a").RouteId("same").To("mock:x");
            b.From("direct:b").RouteId("same").To("mock:x");
        }));
    }

    [Fact]
    public async Task Route_ids_and_node_ids_are_generated()
    {
        await StartWithRoutes(b => b.From("direct:a").To("mock:x").To("mock:y").Id("custom"));

        var route = Context.Routes.Single();
        Assert.StartsWith("route", route.Id);
        var nodes = route.Definition.AllNodes().ToList();
        Assert.Equal("to1", nodes[0].Id);
        Assert.Equal("custom", nodes[1].Id);
    }

    [Fact]
    public async Task Id_on_route_without_steps_sets_route_id()
    {
        await StartWithRoutes(b => b.From("direct:a").Id("myRoute").To("mock:x"));

        Assert.NotNull(Context.GetRoute("myRoute"));
    }

    [Fact]
    public async Task Routes_added_after_start_are_started()
    {
        await Context.StartAsync();
        await Context.AddRoutesAsync(RouteBuilder.Create(b => b.From("direct:late").SetBody(_ => "ok")));

        Assert.Equal("ok", await Template.RequestBodyAsync<string>("direct:late", "x"));
    }

    [Fact]
    public async Task Removed_route_is_gone()
    {
        await StartWithRoutes(b => b.From("direct:a").RouteId("r").To("mock:x"));

        Assert.False(await Context.RemoveRouteAsync("r"));
        await Context.StopRouteAsync("r");
        Assert.True(await Context.RemoveRouteAsync("r"));
        Assert.Null(Context.GetRoute("r"));
    }

    [Fact]
    public async Task Disabled_step_is_skipped()
    {
        await StartWithRoutes(b => b.From("direct:a").SetBody(_ => "changed").Disabled().To("mock:result"));
        Mock("result").ExpectedBodiesReceived("original");

        await Template.SendBodyAsync("direct:a", "original");

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Process_ref_resolves_from_registry()
    {
        Context.Registry.Bind("upper", DelegateProcessor.From(e => e.Message.Body = e.Message.GetBody<string>()!.ToUpperInvariant()));
        await StartWithRoutes(b => b.From("direct:a").ProcessRef("upper"));

        Assert.Equal("ABC", await Template.RequestBodyAsync<string>("direct:a", "abc"));
    }

    [Fact]
    public async Task Exchange_pattern_reflects_template_method()
    {
        await StartWithRoutes(b => b.From("direct:a").To("mock:result"));
        var mock = Mock("result");

        await Template.SendBodyAsync("direct:a", "x");
        await Template.RequestBodyAsync("direct:a", "y");

        Assert.Equal(ExchangePattern.InOnly, mock.ReceivedExchanges[0].Pattern);
        Assert.Equal(ExchangePattern.InOut, mock.ReceivedExchanges[1].Pattern);
    }

    [Fact]
    public async Task Exchange_knows_its_route_and_endpoint()
    {
        await StartWithRoutes(b => b.From("direct:a").RouteId("r").To("mock:result"));

        await Template.SendBodyAsync("direct:a", "x");

        var received = Mock("result").ReceivedExchanges.Single();
        Assert.Equal("r", received.FromRouteId);
        Assert.Equal("direct:a", received.FromEndpoint!.EndpointUri);
        Assert.Equal("mock:result", received.GetProperty<string>(ExchangePropertyKeys.ToEndpoint));
    }

    internal sealed class RecordingNotifier(Action<CaravanEvent> onEvent) : IEventNotifier
    {
        public void Notify(CaravanEvent evt) => onEvent(evt);
    }
}
