using Caravan.Builder;
using Caravan.Components.Mock;
using Microsoft.Extensions.Logging;

namespace Caravan.Core.Tests;

/// <summary>onException semantics (mirrors Camel's OnException* tests).</summary>
public class OnExceptionTests : ContextTestBase
{
    private readonly CapturingLoggerProvider _logs = new();

    public OnExceptionTests()
    {
        Context.LoggerFactory = LoggerFactory.Create(b => b.AddProvider(_logs).SetMinimumLevel(LogLevel.Trace));
    }

    [Fact]
    public async Task Handled_exception_returns_the_result_of_the_block_and_stops_the_route()
    {
        await StartWithRoutes(b =>
        {
            b.OnException<IOException>(o => o.Handled(true)
                .To("mock:error")
                .SetBody(e => "Sorry: " + e.GetProperty<Exception>(ExchangePropertyKeys.ExceptionCaught)!.Message));
            b.From("direct:start").ThrowException(new IOException("Damn")).To("mock:after");
        });

        var reply = await Template.RequestBodyAsync<string>("direct:start", "x");

        Assert.Equal("Sorry: Damn", reply);
        Assert.Equal(0, Mock("after").ReceivedCounter);
        var error = Assert.Single(Mock("error").ReceivedExchanges);
        Assert.Null(error.Exception);
        Assert.IsType<IOException>(error.GetProperty(ExchangePropertyKeys.ExceptionCaught));
        Assert.DoesNotContain(_logs.Messages, m => m.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Not_handled_exception_runs_the_block_then_propagates()
    {
        await StartWithRoutes(b =>
        {
            b.OnException<IOException>(o => o.To("mock:error"));
            b.From("direct:start").ThrowException(new IOException("Damn")).To("mock:after");
        });

        var e = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", "x"));

        Assert.Equal("Damn", e.InnerException!.Message);
        Assert.Equal(1, Mock("error").ReceivedCounter);
        Assert.Equal(0, Mock("after").ReceivedCounter);
        Assert.Contains(_logs.Messages, m => m.Level == LogLevel.Error && m.Message.Contains("Not handled"));
    }

    [Fact]
    public async Task Continued_exception_is_ignored_and_routing_continues_with_the_next_node()
    {
        await StartWithRoutes(b =>
        {
            b.OnException<IOException>(o => o.Continued(true).To("mock:error"));
            b.From("direct:start").To("mock:before").ThrowException(new IOException("ignore me")).To("mock:after");
        });

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.False(exchange.IsFailed);
        Assert.False(exchange.ErrorHandlerHandled);
        Assert.Equal(1, Mock("error").ReceivedCounter);
        var after = Assert.Single(Mock("after").ReceivedExchanges);
        Assert.Equal("ignore me", after.GetProperty<Exception>(ExchangePropertyKeys.ExceptionCaught)!.Message);
    }

    [Fact]
    public async Task Continued_inside_a_nested_block_continues_inside_that_block()
    {
        await StartWithRoutes(b =>
        {
            b.OnException<IOException>(o => o.Continued(true));
            b.From("direct:start")
                .Filter(_ => true, f => f.ThrowException(new IOException()).To("mock:inside"))
                .To("mock:after");
        });

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(1, Mock("inside").ReceivedCounter);
        Assert.Equal(1, Mock("after").ReceivedCounter);
    }

    [Fact]
    public async Task Closest_exception_type_wins()
    {
        await StartWithRoutes(b =>
        {
            b.OnException<Exception>(o => o.Handled(true).To("mock:exception"));
            b.OnException<IOException>(o => o.Handled(true).To("mock:io"));
            b.OnException<SystemException>(o => o.Handled(true).To("mock:system"));
            b.From("direct:start").ThrowException(e => e.Message.GetBody<string>() switch
            {
                "file" => new FileNotFoundException(),
                "io" => new IOException(),
                "arg" => new ArgumentNullException(),
                _ => new Exception(),
            });
        });

        await Template.SendBodyAsync("direct:start", "file");
        await Template.SendBodyAsync("direct:start", "io");
        await Template.SendBodyAsync("direct:start", "arg");
        await Template.SendBodyAsync("direct:start", "other");

        Assert.Equal(["file", "io"], Mock("io").ReceivedBodies);
        Assert.Equal(["arg"], Mock("system").ReceivedBodies);
        Assert.Equal(["other"], Mock("exception").ReceivedBodies);
    }

    [Fact]
    public async Task Inner_exception_is_matched_when_the_outer_one_is_not()
    {
        await StartWithRoutes(b =>
        {
            b.OnException<IOException>(o => o.Handled(true).To("mock:io"));
            b.From("direct:start").ThrowException(new CaravanException("wrapper", new FileNotFoundException("missing")));
        });

        await Template.SendBodyAsync("direct:start", "x");

        var io = Assert.Single(Mock("io").ReceivedExchanges);
        Assert.IsType<CaravanException>(io.GetProperty(ExchangePropertyKeys.ExceptionCaught));
    }

    [Fact]
    public async Task Exact_match_on_the_innermost_exception_wins_over_an_exact_match_on_the_outer_one()
    {
        await StartWithRoutes(b =>
        {
            b.OnException<InvalidOperationException>(o => o.Handled(true).To("mock:outer"));
            b.OnException<IOException>(o => o.Handled(true).To("mock:inner"));
            b.From("direct:start").ThrowException(new InvalidOperationException("outer", new IOException("inner")));
        });

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(1, Mock("inner").ReceivedCounter);
        Assert.Equal(0, Mock("outer").ReceivedCounter);
    }

    [Fact]
    public async Task On_when_selects_between_clauses_for_the_same_type()
    {
        await StartWithRoutes(b =>
        {
            b.OnException<IOException>(o => o.OnWhen(e => e.Message.GetHeader<string>("user") == "admin").Handled(true).To("mock:admin"));
            b.OnException<IOException>(o => o.Handled(true).To("mock:other"));
            b.From("direct:start").ThrowException(new IOException());
        });

        await Template.SendBodyAndHeaderAsync("direct:start", "a", "user", "admin");
        await Template.SendBodyAndHeaderAsync("direct:start", "b", "user", "guest");

        Assert.Equal(["a"], Mock("admin").ReceivedBodies);
        Assert.Equal(["b"], Mock("other").ReceivedBodies);
    }

    [Fact]
    public async Task On_when_that_never_matches_falls_back_to_the_error_handler()
    {
        await StartWithRoutes(b =>
        {
            b.OnException<IOException>(o => o.OnWhen(_ => false).Handled(true).To("mock:never"));
            b.From("direct:start").ThrowException(new IOException());
        });

        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", "x"));
        Assert.Equal(0, Mock("never").ReceivedCounter);
    }

    [Fact]
    public async Task Route_scoped_clause_takes_precedence_over_the_global_one()
    {
        await StartWithRoutes(b =>
        {
            b.OnException<IOException>(o => o.Handled(true).To("mock:global"));
            b.From("direct:scoped").OnException<IOException>(o => o.Handled(true).To("mock:route")).ThrowException(new IOException());
            b.From("direct:plain").ThrowException(new IOException());
        });

        await Template.SendBodyAsync("direct:scoped", "scoped");
        await Template.SendBodyAsync("direct:plain", "plain");

        Assert.Equal(["scoped"], Mock("route").ReceivedBodies);
        Assert.Equal(["plain"], Mock("global").ReceivedBodies);
    }

    [Fact]
    public async Task Global_exact_match_wins_over_a_less_specific_route_scoped_clause()
    {
        // As in Camel: route-scoped clauses are preferred, but an exact type match in a global clause beats an inexact route-scoped one.
        await StartWithRoutes(b =>
        {
            b.OnException<IOException>(o => o.Handled(true).To("mock:global"));
            b.From("direct:start").OnException<Exception>(o => o.Handled(true).To("mock:route")).ThrowException(new IOException());
        });

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(1, Mock("global").ReceivedCounter);
        Assert.Equal(0, Mock("route").ReceivedCounter);
    }

    [Fact]
    public async Task Clause_for_several_types()
    {
        await StartWithRoutes(b =>
        {
            b.OnException([typeof(IOException), typeof(TimeoutException)], o => o.Handled(true).To("mock:error"));
            b.From("direct:start").ThrowException(e => e.Message.GetBody<string>() == "io" ? new IOException() : new TimeoutException());
        });

        await Template.SendBodyAsync("direct:start", "io");
        await Template.SendBodyAsync("direct:start", "timeout");

        Assert.Equal(2, Mock("error").ReceivedCounter);
    }

    [Fact]
    public async Task Clause_redelivery_overrides_the_error_handler_policy()
    {
        var io = 0;
        var other = 0;
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DefaultErrorHandler().MaximumRedeliveries(1).RedeliveryDelay(0));
            b.OnException<IOException>(o => o.MaximumRedeliveries(4));
            b.From("direct:io").Process(_ => throw new IOException("io " + ++io));
            b.From("direct:other").Process(_ => throw new InvalidOperationException("other " + ++other));
        });

        await Template.SendAsync("direct:io", e => e.Message.Body = "x");
        await Template.SendAsync("direct:other", e => e.Message.Body = "x");

        Assert.Equal(5, io);
        Assert.Equal(2, other);
    }

    [Fact]
    public async Task Clause_with_retry_while_and_on_redelivery()
    {
        var attempts = 0;
        var redeliveries = new List<int>();
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DefaultErrorHandler().RedeliveryDelay(0));
            b.OnException<IOException>(o => o
                .RetryWhile(e => e.Message.GetHeader(CaravanHeaders.RedeliveryCounter, 0) < 2)
                .OnRedelivery(e => redeliveries.Add(e.Message.GetHeader<int>(CaravanHeaders.RedeliveryCounter)))
                .Handled(true)
                .SetBody(_ => "gave up"));
            b.From("direct:start").Process(_ => throw new IOException("attempt " + ++attempts));
        });

        var reply = await Template.RequestBodyAsync<string>("direct:start", "x");

        Assert.Equal("gave up", reply);
        Assert.Equal(3, attempts);
        Assert.Equal([1, 2], redeliveries);
    }

    [Fact]
    public async Task Handled_predicate_is_evaluated_per_exchange()
    {
        await StartWithRoutes(b =>
        {
            b.OnException<IOException>(o => o.Handled(e => e.Message.GetHeader<bool>("swallow")).To("mock:error"));
            b.From("direct:start").ThrowException(new IOException());
        });

        await Template.SendBodyAndHeaderAsync("direct:start", "a", "swallow", true);
        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAndHeaderAsync("direct:start", "b", "swallow", false));

        Assert.Equal(2, Mock("error").ReceivedCounter);
    }

    [Fact]
    public async Task Clause_can_use_the_original_message()
    {
        await StartWithRoutes(b =>
        {
            b.OnException<IOException>(o => o.UseOriginalMessage().Handled(true).To("mock:error"));
            b.From("direct:start").SetBody(_ => "changed").ThrowException(new IOException());
        });

        await Template.SendBodyAsync("direct:start", "original");

        Assert.Equal(["original"], Mock("error").ReceivedBodies);
    }

    [Fact]
    public async Task New_exception_inside_the_block_fails_the_exchange_without_being_handled_again()
    {
        await StartWithRoutes(b =>
        {
            b.OnException<InvalidOperationException>(o => o.Handled(true).To("mock:a").ThrowException(new IOException("Damn in handler")).To("mock:b"));
            b.OnException<IOException>(o => o.Handled(true).To("mock:io"));
            b.From("direct:start").ThrowException(new InvalidOperationException("first"));
        });

        var e = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", "x"));

        Assert.Equal("Damn in handler", e.InnerException!.Message);
        Assert.Equal(1, Mock("a").ReceivedCounter);
        Assert.Equal(0, Mock("b").ReceivedCounter);
        Assert.Equal(0, Mock("io").ReceivedCounter);
        Assert.Contains(_logs.Messages, m => m.Level == LogLevel.Error && m.Message.Contains("while trying to handle previously thrown exception"));
    }

    [Fact]
    public async Task Block_can_call_another_route()
    {
        await StartWithRoutes(b =>
        {
            b.OnException<IOException>(o => o.Handled(true).To("direct:handler"));
            b.From("direct:start").ThrowException(new IOException("x"));
            b.From("direct:handler").SetBody(_ => "from handler").To("mock:handler");
        });

        var reply = await Template.RequestBodyAsync<string>("direct:start", "x");

        Assert.Equal("from handler", reply);
        Assert.Equal(1, Mock("handler").ReceivedCounter);
    }

    [Fact]
    public async Task Exception_without_a_clause_uses_the_error_handler()
    {
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead"));
            b.OnException<IOException>(o => o.Handled(true).To("mock:io"));
            b.From("direct:start").ThrowException(new InvalidOperationException());
        });

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(1, Mock("dead").ReceivedCounter);
        Assert.Equal(0, Mock("io").ReceivedCounter);
    }

    [Fact]
    public async Task Clause_applies_to_every_route_of_its_builder_only()
    {
        Context.AddRoutes(RouteBuilder.Create(b =>
        {
            b.OnException<IOException>(o => o.Handled(true).To("mock:handled"));
            b.From("direct:a").ThrowException(new IOException());
            b.From("direct:b").ThrowException(new IOException());
        }));
        Context.AddRoutes(RouteBuilder.Create(b => b.From("direct:c").ThrowException(new IOException())));
        await Context.StartAsync();

        await Template.SendBodyAsync("direct:a", "a");
        await Template.SendBodyAsync("direct:b", "b");
        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:c", "c"));

        Mock("handled").ExpectedBodiesReceived("a", "b");
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Handled_clause_logs_only_when_asked()
    {
        await StartWithRoutes(b =>
        {
            b.OnException<IOException>(o => o.Handled(true).LogHandled());
            b.From("direct:start").ThrowException(new IOException("logged"));
        });

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Contains(_logs.Messages, m => m.Level == LogLevel.Error && m.Message.Contains("Handled by the onException clause"));
    }
}
