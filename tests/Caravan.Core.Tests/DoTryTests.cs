using Caravan.Builder;
using Caravan.Components.Mock;

namespace Caravan.Core.Tests;

/// <summary>doTry/doCatch/doFinally and throwException (mirrors Camel's TryProcessor tests).</summary>
public class DoTryTests : ContextTestBase
{
    [Fact]
    public async Task Catch_handles_the_exception_and_routing_continues_after_the_try()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .DoTry(
                t => t.To("mock:try").ThrowException(new IOException("Forced")).To("mock:never"),
                c => c.DoCatch<IOException>(r => r.To("mock:catch").SetBody(_ => "caught")))
            .To("mock:after"));

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.False(exchange.IsFailed);
        Assert.Equal(0, Mock("never").ReceivedCounter);
        var caught = Assert.Single(Mock("catch").ReceivedExchanges);
        Assert.Equal("Forced", caught.GetProperty<Exception>(ExchangePropertyKeys.ExceptionCaught)!.Message);
        Assert.Equal("mock:try", caught.GetProperty<string>(ExchangePropertyKeys.FailureEndpoint));
        Assert.Equal(["caught"], Mock("after").ReceivedBodies);
    }

    [Fact]
    public async Task First_matching_catch_wins_and_on_when_filters()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .DoTry(
                t => t.ThrowException(e => e.Message.GetBody<string>() == "file" ? new FileNotFoundException() : new IOException()),
                c => c
                    .DoCatch<FileNotFoundException>(r => r.To("mock:never"), onWhen: _ => false)
                    .DoCatch([typeof(TimeoutException), typeof(FileNotFoundException)], r => r.To("mock:file"))
                    .DoCatch<IOException>(r => r.To("mock:io"))
                    .DoCatch<Exception>(r => r.To("mock:any"))));

        await Template.SendBodyAsync("direct:start", "file");
        await Template.SendBodyAsync("direct:start", "io");

        Assert.Equal(["file"], Mock("file").ReceivedBodies);
        Assert.Equal(["io"], Mock("io").ReceivedBodies);
        Assert.Equal(0, Mock("any").ReceivedCounter);
        Assert.Equal(0, Mock("never").ReceivedCounter);
    }

    [Fact]
    public async Task Catch_matches_inner_exceptions()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .DoTry(
                t => t.ThrowException(new CaravanException("wrapper", new IOException("cause"))),
                c => c.DoCatch<IOException>(r => r.To("mock:io"))));

        await Template.SendBodyAsync("direct:start", "x");

        var io = Assert.Single(Mock("io").ReceivedExchanges);
        Assert.IsType<CaravanException>(io.GetProperty(ExchangePropertyKeys.ExceptionCaught));
    }

    [Fact]
    public async Task Finally_runs_on_success_and_on_failure()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .DoTry(
                t => t.Filter(Expr.Body().IsEqualTo("fail"), f => f.ThrowException(new IOException())),
                c => c.DoCatch<IOException>(r => r.To("mock:catch")).DoFinally(r => r.To("mock:finally")))
            .To("mock:after"));

        await Template.SendBodyAsync("direct:start", "ok");
        await Template.SendBodyAsync("direct:start", "fail");

        Assert.Equal(["fail"], Mock("catch").ReceivedBodies);
        Assert.Equal(["ok", "fail"], Mock("finally").ReceivedBodies);
        Assert.Equal(["ok", "fail"], Mock("after").ReceivedBodies);
    }

    [Fact]
    public async Task Uncaught_exception_runs_finally_then_propagates_without_on_exception_or_redelivery()
    {
        var attempts = 0;
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DefaultErrorHandler().MaximumRedeliveries(3).RedeliveryDelay(0));
            b.OnException<InvalidOperationException>(o => o.Handled(true).To("mock:on-exception"));
            b.From("direct:start")
                .DoTry(
                    t => t.Process(_ => throw new InvalidOperationException("attempt " + ++attempts)),
                    c => c.DoCatch<IOException>(r => r.To("mock:catch")).DoFinally(r => r.To("mock:finally")))
                .To("mock:after");
        });

        var e = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", "x"));

        Assert.Equal("attempt 1", e.InnerException!.Message);
        Assert.Equal(1, attempts);
        Assert.Equal(1, Mock("finally").ReceivedCounter);
        Assert.Equal(0, Mock("catch").ReceivedCounter);
        Assert.Equal(0, Mock("on-exception").ReceivedCounter);
        Assert.Equal(0, Mock("after").ReceivedCounter);
    }

    [Fact]
    public async Task Finally_sees_the_pending_exception_as_caught()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .DoTry(
                t => t.ThrowException(new IOException("pending")),
                c => c.DoFinally(r => r.To("mock:finally"))));

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.Equal("pending", exchange.Exception!.Message);
        var f = Assert.Single(Mock("finally").ReceivedExchanges);
        Assert.Null(f.Exception);
        Assert.Equal("pending", f.GetProperty<Exception>(ExchangePropertyKeys.ExceptionCaught)!.Message);
    }

    [Fact]
    public async Task Rethrow_from_catch_runs_finally_and_propagates_the_new_exception()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .DoTry(
                t => t.ThrowException(new IOException("first")),
                c => c
                    .DoCatch<IOException>(r => r.To("mock:catch").ThrowException(e => new InvalidOperationException("rethrown", e.GetProperty<Exception>(ExchangePropertyKeys.ExceptionCaught))))
                    .DoFinally(r => r.To("mock:finally")))
            .To("mock:after"));

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.Equal("rethrown", exchange.Exception!.Message);
        Assert.IsType<IOException>(exchange.Exception.InnerException);
        Assert.Equal(1, Mock("finally").ReceivedCounter);
        Assert.Equal(0, Mock("after").ReceivedCounter);
    }

    [Fact]
    public async Task Exception_thrown_by_finally_with_a_pending_exception_keeps_the_pending_one()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .DoTry(
                t => t.ThrowException(new IOException("pending")),
                c => c.DoFinally(r => r.ThrowException(new InvalidOperationException("from finally")))));

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.Equal("pending", exchange.Exception!.Message);
    }

    [Fact]
    public async Task Exception_thrown_by_finally_after_success_fails_the_exchange()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .DoTry(t => t.To("mock:try"), c => c.DoFinally(r => r.ThrowException(new InvalidOperationException("from finally"))))
            .To("mock:after"));

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.Equal("from finally", exchange.Exception!.Message);
        Assert.Equal(0, Mock("after").ReceivedCounter);
    }

    [Fact]
    public async Task Nested_try_lets_uncaught_exceptions_reach_the_outer_catch()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .DoTry(
                outer => outer
                    .DoTry(
                        inner => inner.ThrowException(new IOException("deep")),
                        c => c.DoCatch<TimeoutException>(r => r.To("mock:inner-catch")).DoFinally(r => r.To("mock:inner-finally")))
                    .To("mock:never"),
                c => c.DoCatch<IOException>(r => r.To("mock:outer-catch")))
            .To("mock:after"));

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(0, Mock("inner-catch").ReceivedCounter);
        Assert.Equal(1, Mock("inner-finally").ReceivedCounter);
        Assert.Equal(0, Mock("never").ReceivedCounter);
        Assert.Equal(1, Mock("outer-catch").ReceivedCounter);
        Assert.Equal(1, Mock("after").ReceivedCounter);
    }

    [Fact]
    public async Task Exception_from_a_sub_route_is_caught()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start")
                .DoTry(t => t.To("direct:sub"), c => c.DoCatch<IOException>(r => r.SetBody(_ => "recovered")))
                .To("mock:after");
            b.From("direct:sub").ErrorHandler(b.NoErrorHandler()).ThrowException(new IOException("sub"));
        });

        var reply = await Template.RequestBodyAsync<string>("direct:start", "x");

        Assert.Equal("recovered", reply);
        Assert.Equal(1, Mock("after").ReceivedCounter);
    }

    [Fact]
    public async Task Exhausted_failure_of_a_sub_route_with_an_error_handler_is_caught_too()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start")
                .DoTry(t => t.To("direct:sub"), c => c.DoCatch<IOException>(r => r.To("mock:catch")))
                .To("mock:after");
            b.From("direct:sub").ThrowException(new IOException("sub"));
        });

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.False(exchange.IsFailed);
        Assert.False(exchange.IsRedeliveryExhausted);
        Assert.Equal(1, Mock("catch").ReceivedCounter);
        Assert.Equal(1, Mock("after").ReceivedCounter);
    }

    [Fact]
    public async Task Try_without_catch_or_finally_fails_to_start()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.From("direct:start").DoTry(t => t.To("mock:a"), _ => { })));

        await Assert.ThrowsAsync<FailedToCreateRouteException>(() => Context.StartAsync());
    }

    [Fact]
    public void Catch_after_finally_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => RouteBuilder.Create(b => b.From("direct:start")
                .DoTry(t => t.To("mock:a"), c => c.DoFinally(_ => { }).DoCatch<IOException>(_ => { })))
            .Build(Context));
    }

    [Fact]
    public async Task Throw_exception_with_type_and_message_creates_a_new_exception_each_time()
    {
        await StartWithRoutes(b => b.From("direct:start").ThrowException(typeof(IOException), "Forced"));

        var first = await Template.SendAsync("direct:start", e => e.Message.Body = "a");
        var second = await Template.SendAsync("direct:start", e => e.Message.Body = "b");

        Assert.IsType<IOException>(first.Exception);
        Assert.Equal("Forced", first.Exception!.Message);
        Assert.NotSame(first.Exception, second.Exception);
    }

    [Fact]
    public void Throw_exception_requires_an_exception_type_with_a_message_constructor()
    {
        Assert.Throws<ArgumentException>(() => RouteBuilder.Create(b => b.From("direct:start").ThrowException(typeof(string), "x")).Build(Context));
    }

    [Fact]
    public async Task Throw_exception_factory_sees_the_exchange()
    {
        await StartWithRoutes(b => b.From("direct:start").ThrowException(e => new InvalidOperationException("Bad: " + e.Message.Body)));

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "data");

        Assert.Equal("Bad: data", exchange.Exception!.Message);
    }
}
