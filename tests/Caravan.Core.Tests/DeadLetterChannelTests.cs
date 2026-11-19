using Caravan.Builder;
using Caravan.Components.Mock;
using Caravan.Spi;
using Microsoft.Extensions.Logging;

namespace Caravan.Core.Tests;

/// <summary>Dead letter channel semantics (mirrors Camel's DeadLetterChannel tests).</summary>
public class DeadLetterChannelTests : ContextTestBase
{
    private readonly CapturingLoggerProvider _logs = new();
    private readonly TestTimeProvider _time = new();

    public DeadLetterChannelTests()
    {
        Context.LoggerFactory = LoggerFactory.Create(b => b.AddProvider(_logs).SetMinimumLevel(LogLevel.Trace));
        Context.TimeProvider = _time;
    }

    [Fact]
    public async Task Failure_goes_to_the_dead_letter_endpoint_and_is_handled()
    {
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead"));
            b.From("direct:start").RouteId("main").To("mock:a").ThrowException(new InvalidOperationException("Forced")).To("mock:after");
        });

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "Hello");

        Assert.False(exchange.IsFailed);
        Assert.True(exchange.ErrorHandlerHandled);
        Assert.Equal(0, Mock("after").ReceivedCounter);
        var dead = Assert.Single(Mock("dead").ReceivedExchanges);
        Assert.Equal("Hello", dead.Message.Body);
        Assert.Null(dead.Exception);
        Assert.Equal("Forced", dead.GetProperty<Exception>(ExchangePropertyKeys.ExceptionCaught)!.Message);
        Assert.Equal("mock:a", dead.GetProperty<string>(ExchangePropertyKeys.FailureEndpoint));
        Assert.Equal("main", dead.GetProperty<string>(ExchangePropertyKeys.FailureRouteId));
        Assert.Equal(_time.GetUtcNow(), dead.GetProperty<DateTimeOffset>(ExchangePropertyKeys.FailureTimestamp));
        Assert.Contains(_logs.Messages, m => m.Level == LogLevel.Error && m.Message.Contains("Handled by DeadLetterChannel: [mock:dead]"));
    }

    [Fact]
    public async Task Send_body_does_not_throw_and_request_returns_the_body_at_failure()
    {
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead"));
            b.From("direct:start").SetBody(_ => "changed").ThrowException(new InvalidOperationException());
        });

        await Template.SendBodyAsync("direct:start", "a");
        var reply = await Template.RequestBodyAsync<string>("direct:start", "b");

        Assert.Equal("changed", reply);
        Assert.Equal(2, Mock("dead").ReceivedCounter);
    }

    [Fact]
    public async Task Redeliveries_happen_before_the_dead_letter_channel()
    {
        var attempts = 0;
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead").MaximumRedeliveries(2).RedeliveryDelay(0));
            b.From("direct:start").Process(_ => throw new InvalidOperationException("attempt " + ++attempts));
        });

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.Equal(3, attempts);
        var dead = Assert.Single(Mock("dead").ReceivedExchanges);
        Assert.Equal(2, dead.Message.GetHeader<int>(CaravanHeaders.RedeliveryCounter));
        Assert.Equal("attempt 3", dead.GetProperty<Exception>(ExchangePropertyKeys.ExceptionCaught)!.Message);
        Assert.False(exchange.Message.HasHeader(CaravanHeaders.RedeliveryCounter));
        Assert.False(exchange.IsFailed);
    }

    [Fact]
    public async Task Use_original_message_sends_the_message_as_received()
    {
        Assert.False(Context.AllowUseOriginalMessage);
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead").UseOriginalMessage());
            b.From("direct:start").SetBody(_ => "changed").SetHeader("added", Expr.Constant(1)).ThrowException(new InvalidOperationException());
        });

        await Template.SendBodyAndHeaderAsync("direct:start", "original", "h", "v");

        Assert.True(Context.AllowUseOriginalMessage);
        var dead = Assert.Single(Mock("dead").ReceivedExchanges);
        Assert.Equal("original", dead.Message.Body);
        Assert.Equal("v", dead.Message.GetHeader("h"));
        Assert.False(dead.Message.HasHeader("added"));
    }

    [Fact]
    public async Task Use_original_body_keeps_the_current_headers()
    {
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead").UseOriginalBody());
            b.From("direct:start").SetBody(_ => "changed").SetHeader("added", Expr.Constant(1)).ThrowException(new InvalidOperationException());
        });

        await Template.SendBodyAsync("direct:start", "original");

        var dead = Assert.Single(Mock("dead").ReceivedExchanges);
        Assert.Equal("original", dead.Message.Body);
        Assert.Equal(1, dead.Message.GetHeader("added"));
    }

    [Fact]
    public async Task Use_original_message_from_a_sub_route_is_the_message_received_by_the_first_route()
    {
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead").UseOriginalMessage());
            b.From("direct:start").SetBody(_ => "first change").To("direct:sub");
            b.From("direct:sub").ErrorHandler(b.NoErrorHandler()).SetBody(_ => "second change").ThrowException(new InvalidOperationException());
        });

        await Template.SendBodyAsync("direct:start", "original");

        Assert.Equal(["original"], Mock("dead").ReceivedBodies);
    }

    [Fact]
    public async Task On_prepare_failure_prepares_the_exchange_for_the_dead_letter_endpoint()
    {
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead")
                .OnPrepareFailure(e => e.Message.SetHeader("reason", e.GetProperty<Exception>(ExchangePropertyKeys.ExceptionCaught)!.Message)));
            b.From("direct:start").ThrowException(new InvalidOperationException("bad input"));
        });

        await Template.SendBodyAsync("direct:start", "x");

        Mock("dead").ExpectedHeaderReceived("reason", "bad input");
        await Mock("dead").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task New_exception_in_the_dead_letter_endpoint_is_handled_by_default()
    {
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("direct:dead"));
            b.From("direct:start").ThrowException(new InvalidOperationException("first"));
            b.From("direct:dead").ErrorHandler(b.NoErrorHandler()).ThrowException(new IOException("dead letter broken"));
        });

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.False(exchange.IsFailed);
        Assert.Contains(_logs.Messages, m => m.Level == LogLevel.Warning && m.Message.Contains("received new exception"));
    }

    [Fact]
    public async Task New_exception_in_the_dead_letter_endpoint_fails_the_exchange_when_not_handled()
    {
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("direct:dead").DeadLetterHandleNewException(false));
            b.From("direct:start").ThrowException(new InvalidOperationException("first"));
            b.From("direct:dead").ErrorHandler(b.NoErrorHandler()).ThrowException(new IOException("dead letter broken"));
        });

        var e = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", "x"));

        Assert.Equal("dead letter broken", e.InnerException!.Message);
    }

    [Fact]
    public async Task On_exception_clause_that_does_not_handle_overrides_the_dead_letter_channel()
    {
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead"));
            b.OnException<IOException>(o => o.To("mock:io"));
            b.From("direct:start").ThrowException(new IOException("io"));
        });

        var e = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", "x"));

        Assert.IsType<IOException>(e.InnerException);
        Assert.Equal(1, Mock("io").ReceivedCounter);
        Assert.Equal(0, Mock("dead").ReceivedCounter);
    }

    [Fact]
    public async Task On_exception_clause_without_steps_uses_its_redelivery_then_the_dead_letter_channel()
    {
        var attempts = 0;
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead").RedeliveryDelay(0));
            b.OnException<IOException>(o => o.MaximumRedeliveries(3));
            b.From("direct:start").Process(_ => throw new IOException("attempt " + ++attempts));
        });

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(4, attempts);
        Assert.Equal(1, Mock("dead").ReceivedCounter);
    }

    [Fact]
    public async Task Failure_handled_event_is_raised()
    {
        var events = new List<CaravanEvent>();
        Context.EventNotifiers.Add(new CoreRoutingTests.RecordingNotifier(events.Add));
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead"));
            b.From("direct:start").ThrowException(new InvalidOperationException("x"));
        });

        await Template.SendBodyAsync("direct:start", "x");

        var handled = Assert.Single(events, e => e.Type == CaravanEventType.ExchangeFailureHandled);
        Assert.Equal("mock:dead", handled.Endpoint!.EndpointUri);
        Assert.IsType<InvalidOperationException>(handled.Cause);
        Assert.Contains(events, e => e.Type == CaravanEventType.ExchangeCompleted);
    }

    [Fact]
    public async Task Dead_letter_channel_on_the_context_applies_to_all_routes()
    {
        Context.ErrorHandler = new Model.DeadLetterChannelDefinition("mock:dead");
        await StartWithRoutes(b =>
        {
            b.From("direct:a").ThrowException(new InvalidOperationException());
            b.From("direct:b").ThrowException(new InvalidOperationException());
        });

        await Template.SendBodyAsync("direct:a", "a");
        await Template.SendBodyAsync("direct:b", "b");

        Mock("dead").ExpectedBodiesReceived("a", "b");
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }
}
