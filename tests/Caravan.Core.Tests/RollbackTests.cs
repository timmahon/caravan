using Caravan.Builder;

namespace Caravan.Core.Tests;

/// <summary>rollback, markRollbackOnly and markRollbackOnlyLast.</summary>
public class RollbackTests : ContextTestBase
{
    [Fact]
    public async Task Rollback_fails_the_exchange_without_redelivery()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .ErrorHandler(b.DefaultErrorHandler().MaximumRedeliveries(3).RedeliveryDelay(0))
            .To("mock:before")
            .Rollback("That do not work")
            .To("mock:after"));

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        var e = Assert.IsType<RollbackExchangeException>(exchange.Exception);
        Assert.StartsWith("That do not work", e.Message);
        Assert.True(exchange.IsRollbackOnly);
        Assert.Equal(1, Mock("before").ReceivedCounter);
        Assert.Equal(0, Mock("after").ReceivedCounter);
        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", "y"));
    }

    [Fact]
    public async Task Mark_rollback_only_stops_routing_without_an_exception_and_fails_the_unit_of_work()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .OnCompletion(c => c.OnFailureOnly().To("mock:rolled-back"))
            .To("mock:before")
            .MarkRollbackOnly()
            .To("mock:after"));

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.Null(exchange.Exception);
        Assert.True(exchange.IsRollbackOnly);
        Assert.Equal(0, Mock("after").ReceivedCounter);
        Assert.Equal(1, Mock("rolled-back").ReceivedCounter);
    }

    [Fact]
    public async Task Mark_rollback_only_last_stops_only_the_current_route()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start").To("direct:sub").To("mock:caller-after");
            b.From("direct:sub").To("mock:sub-before").MarkRollbackOnlyLast().To("mock:sub-after");
        });

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.False(exchange.IsRollbackOnly);
        Assert.False(exchange.IsRollbackOnlyLast);
        Assert.Equal(1, Mock("sub-before").ReceivedCounter);
        Assert.Equal(0, Mock("sub-after").ReceivedCounter);
        Assert.Equal(1, Mock("caller-after").ReceivedCounter);
    }

    [Fact]
    public async Task Mark_rollback_only_in_a_sub_route_stops_the_caller_too()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start").To("direct:sub").To("mock:caller-after");
            b.From("direct:sub").MarkRollbackOnly();
        });

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.True(exchange.IsRollbackOnly);
        Assert.Equal(0, Mock("caller-after").ReceivedCounter);
    }

    [Fact]
    public async Task Rollback_can_be_caught()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .DoTry(t => t.Rollback(), c => c.DoCatch<RollbackExchangeException>(r => r.To("mock:catch")))
            .To("mock:after"));

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.False(exchange.IsFailed);
        Assert.False(exchange.IsRollbackOnly);
        Assert.Equal(1, Mock("catch").ReceivedCounter);
        Assert.Equal(1, Mock("after").ReceivedCounter);
    }

    [Fact]
    public async Task Rollback_goes_to_the_dead_letter_channel()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .ErrorHandler(b.DeadLetterChannel("mock:dead").MaximumRedeliveries(2).RedeliveryDelay(0))
            .Rollback());

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.False(exchange.IsFailed);
        Assert.False(exchange.IsRollbackOnly);
        var dead = Assert.Single(Mock("dead").ReceivedExchanges);
        Assert.IsType<RollbackExchangeException>(dead.GetProperty(ExchangePropertyKeys.ExceptionCaught));
    }
}
