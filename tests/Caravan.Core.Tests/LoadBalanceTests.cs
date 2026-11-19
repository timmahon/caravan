using Caravan.Builder;
using Caravan.Components.Mock;
using Caravan.LoadBalancing;
using Caravan.Spi;

namespace Caravan.Core.Tests;

/// <summary>loadBalance semantics (mirrors Camel's RoundRobin/Random/Weighted/Failover/Sticky/Topic load balancer tests).</summary>
public class LoadBalanceTests : ContextTestBase
{
    private async Task SendBodies(string uri, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            await Template.SendBodyAsync(uri, i);
        }
    }

    private void Fail(string mock, Func<Exception> exception) => Mock(mock).WhenAnyExchangeReceived(_ => throw exception());

    [Fact]
    public async Task Round_robin_sends_to_each_output_in_turn()
    {
        await StartWithRoutes(b => b.From("direct:start").LoadBalance(lb => lb.RoundRobin(), o => o.To("mock:x").To("mock:y").To("mock:z")).To("mock:result"));
        Mock("x").ExpectedBodiesReceived(1, 4);
        Mock("y").ExpectedBodiesReceived(2, 5);
        Mock("z").ExpectedBodiesReceived(3, 6);
        Mock("result").ExpectedMessageCount(6);

        await SendBodies("direct:start", 6);

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Round_robin_over_pipelines_and_with_a_single_output()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:pipelines").LoadBalance(lb => lb.RoundRobin(), o => o.Pipeline(p => p.Transform(e => e.Message.Body + "!").To("mock:a")).To("mock:b"));
            b.From("direct:single").LoadBalance(lb => lb.RoundRobin(), o => o.To("mock:only"));
        });
        Mock("a").ExpectedBodiesReceived("1!", "3!");
        Mock("b").ExpectedBodiesReceived(2);
        Mock("only").ExpectedMessageCount(2);

        await SendBodies("direct:pipelines", 3);
        await SendBodies("direct:single", 2);

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Random_uses_every_output()
    {
        await StartWithRoutes(b => b.From("direct:start").LoadBalance(lb => lb.Random(), o => o.To("mock:x").To("mock:y").To("mock:z")));

        await SendBodies("direct:start", 150);

        Assert.All(new[] { "x", "y", "z" }, m => Assert.True(Mock(m).ReceivedCounter > 0));
        Assert.Equal(150, Mock("x").ReceivedCounter + Mock("y").ReceivedCounter + Mock("z").ReceivedCounter);
    }

    [Fact]
    public async Task Weighted_round_robin_follows_the_distribution_ratio()
    {
        await StartWithRoutes(b => b.From("direct:start").LoadBalance(lb => lb.Weighted(true, 4, 2, 1), o => o.To("mock:x").To("mock:y").To("mock:z")));
        Mock("x").ExpectedBodiesReceived(1, 4, 6, 7, 8, 11, 13, 14);
        Mock("y").ExpectedBodiesReceived(2, 5, 9, 12);
        Mock("z").ExpectedBodiesReceived(3, 10);

        await SendBodies("direct:start", 14);

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Weighted_random_gives_each_output_its_weight_per_cycle()
    {
        await StartWithRoutes(b => b.From("direct:start").LoadBalance(lb => lb.Weighted(false, "2, 3,1"), o => o.To("mock:x").To("mock:y").To("mock:z")));

        await SendBodies("direct:start", 12);

        Assert.Equal(4, Mock("x").ReceivedCounter);
        Assert.Equal(6, Mock("y").ReceivedCounter);
        Assert.Equal(2, Mock("z").ReceivedCounter);
    }

    [Fact]
    public async Task Weighted_with_a_wrong_number_of_weights_fails_the_route()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.From("direct:start").LoadBalance(lb => lb.Weighted(true, 1, 2), o => o.To("mock:x"))));

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Context.StartAsync());

        Assert.Contains("does not match the number of processors", ex.ToString());
    }

    [Fact]
    public async Task Failover_tries_the_next_output_on_any_exception()
    {
        await StartWithRoutes(b => b.From("direct:start").LoadBalance(lb => lb.Failover(), o => o.To("mock:x").To("mock:y").To("mock:z")).To("mock:result"));
        Fail("x", () => new IOException("x down"));
        Fail("y", () => new InvalidOperationException("y down"));
        Mock("z").ExpectedBodiesReceived(1, 2);
        Mock("result").ExpectedBodiesReceived(1, 2);

        await SendBodies("direct:start", 2);

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        Assert.Equal(2, Mock("x").ReceivedCounter);
        Assert.Equal(2, Mock("y").ReceivedCounter);
    }

    [Fact]
    public async Task Failover_only_on_the_given_exceptions_including_causes()
    {
        await StartWithRoutes(b => b.From("direct:start").LoadBalance(lb => lb.Failover(typeof(IOException)), o => o.To("mock:x").To("mock:y")));
        var wrapped = true;
        Mock("x").WhenAnyExchangeReceived(_ => throw (wrapped
            ? new InvalidOperationException("wrapper", new IOException("cause"))
            : new InvalidOperationException("not io")));
        Mock("y").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "wrapped io");
        wrapped = false;
        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", "other"));

        Assert.Equal("not io", ex.InnerException!.Message);
        await Mock("y").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Failover_without_round_robin_stops_after_the_last_output()
    {
        await StartWithRoutes(b => b.From("direct:start").LoadBalance(lb => lb.Failover(), o => o.To("mock:x").To("mock:y")));
        Fail("x", () => new IOException("x"));
        Fail("y", () => new IOException("y"));

        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", 1));

        Assert.Equal("y", ex.InnerException!.Message);
        Assert.Equal(1, Mock("x").ReceivedCounter);
        Assert.Equal(1, Mock("y").ReceivedCounter);
    }

    [Fact]
    public async Task Failover_round_robin_wraps_around_up_to_the_maximum_attempts()
    {
        await StartWithRoutes(b => b.From("direct:start").LoadBalance(lb => lb.Failover(maximumFailoverAttempts: 4, roundRobin: true), o => o.To("mock:x").To("mock:y")));
        Fail("x", () => new IOException("x"));
        Fail("y", () => new IOException("y"));

        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", 1));

        // One attempt plus four failovers: x, y, x, y, x.
        Assert.Equal("x", ex.InnerException!.Message);
        Assert.Equal(3, Mock("x").ReceivedCounter);
        Assert.Equal(2, Mock("y").ReceivedCounter);
    }

    [Fact]
    public async Task Failover_with_zero_attempts_does_not_fail_over()
    {
        await StartWithRoutes(b => b.From("direct:start").LoadBalance(lb => lb.Failover(maximumFailoverAttempts: 0), o => o.To("mock:x").To("mock:y")));
        Fail("x", () => new IOException("x"));
        ExpectNone("y");

        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", 1));

        await Mock("y").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Failover_round_robin_starts_each_exchange_with_the_next_output()
    {
        await StartWithRoutes(b => b.From("direct:start").LoadBalance(lb => lb.Failover(roundRobin: true), o => o.To("mock:x").To("mock:y").To("mock:z")));
        Fail("y", () => new IOException("y"));
        Mock("x").ExpectedBodiesReceived(1, 4);
        Mock("y").ExpectedBodiesReceived(2, 5);
        Mock("z").ExpectedBodiesReceived(2, 3, 5);

        await SendBodies("direct:start", 5);

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Failover_sticky_starts_with_the_last_good_output()
    {
        await StartWithRoutes(b => b.From("direct:start").LoadBalance(lb => lb.Failover(sticky: true), o => o.To("mock:x").To("mock:y").To("mock:z")));
        var xDown = true;
        Mock("x").WhenAnyExchangeReceived(_ =>
        {
            if (xDown)
            {
                throw new IOException("x");
            }
        });
        Fail("y", () => new IOException("y"));

        await SendBodies("direct:start", 2);
        xDown = false;
        await Template.SendBodyAsync("direct:start", 3);

        Assert.Equal([1, 2, 3], Mock("z").GetReceivedBodies<int>());
        Assert.Equal(1, Mock("x").ReceivedCounter);
        Assert.Equal(1, Mock("y").ReceivedCounter);
    }

    [Fact]
    public async Task Failover_each_attempt_starts_from_the_original_message()
    {
        await StartWithRoutes(b => b.From("direct:start").LoadBalance(lb => lb.Failover(), o => o
            .Pipeline(p => p.Transform(_ => "changed").SetHeader("dirty", _ => true).ThrowException(new IOException("x")))
            .To("mock:y")));
        Mock("y").ExpectedBodiesReceived("original");
        Mock("y").Expect(m => m.ReceivedExchanges[0].Message.HasHeader("dirty") ? "dirty header leaked" : null);

        await Template.SendBodyAsync("direct:start", "original");

        await Mock("y").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Failover_inherits_the_error_handler_by_default()
    {
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DefaultErrorHandler().MaximumRedeliveries(2).RedeliveryDelay(0));
            b.From("direct:inherit").LoadBalance(lb => lb.Failover(), o => o.To("mock:x").To("mock:y"));
            b.From("direct:own").LoadBalance(lb => lb.Failover(inheritErrorHandler: false), o => o.To("mock:a").To("mock:b"));
        });
        Fail("x", () => new IOException("x"));
        Fail("a", () => new IOException("a"));
        Mock("y").ExpectedMessageCount(1);
        Mock("b").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:inherit", 1);
        await Template.SendBodyAsync("direct:own", 1);

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        Assert.Equal(3, Mock("x").ReceivedCounter);
        Assert.Equal(1, Mock("a").ReceivedCounter);
    }

    [Fact]
    public async Task Failure_handled_by_an_inherited_dead_letter_channel_does_not_fail_over()
    {
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead"));
            b.From("direct:start").LoadBalance(lb => lb.Failover(), o => o.To("mock:x").To("mock:y")).To("mock:result");
        });
        Fail("x", () => new IOException("x"));
        Mock("dead").ExpectedMessageCount(1);
        ExpectNone("y");
        ExpectNone("result");

        await Template.SendBodyAsync("direct:start", 1);

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Sticky_sends_the_same_correlation_value_to_the_same_output()
    {
        await StartWithRoutes(b => b.From("direct:start").LoadBalance(lb => lb.Sticky(Expr.Header("customer")), o => o.To("mock:x").To("mock:y").To("mock:z")));

        foreach (var customer in new[] { "a", "b", "a", "c", "b", "a", "d" })
        {
            await Template.SendBodyAndHeaderAsync("direct:start", customer, "customer", customer);
        }

        Assert.Equal(["a", "a", "a", "d"], Mock("x").GetReceivedBodies<string>());
        Assert.Equal(["b", "b"], Mock("y").GetReceivedBodies<string>());
        Assert.Equal(["c"], Mock("z").GetReceivedBodies<string>());
    }

    [Fact]
    public async Task Sticky_with_a_function_and_null_values_round_robin()
    {
        await StartWithRoutes(b => b.From("direct:start").LoadBalance(lb => lb.Sticky(e => e.Message.GetHeader("k")), o => o.To("mock:x").To("mock:y")));

        await Template.SendBodyAsync("direct:start", 1);
        await Template.SendBodyAsync("direct:start", 2);
        await Template.SendBodyAndHeaderAsync("direct:start", 3, "k", "same");
        await Template.SendBodyAndHeaderAsync("direct:start", 4, "k", "same");

        Assert.Equal([1, 3, 4], Mock("x").GetReceivedBodies<int>());
        Assert.Equal([2], Mock("y").GetReceivedBodies<int>());
    }

    [Fact]
    public async Task Topic_sends_a_copy_to_every_output_and_leaves_the_exchange_unchanged()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .LoadBalance(lb => lb.Topic(), o => o.Pipeline(p => p.Transform(_ => "changed").To("mock:x")).To("mock:y").To("mock:z"))
            .To("mock:result"));
        Mock("x").ExpectedBodiesReceived("changed");
        Mock("y").ExpectedBodiesReceived("hello");
        Mock("z").ExpectedBodiesReceived("hello");
        Mock("result").ExpectedBodiesReceived("hello");

        var sent = await Template.SendAsync("direct:start", e => e.Message.Body = "hello");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        Assert.Equal(sent.ExchangeId, Mock("y").ReceivedExchanges[0].GetProperty(ExchangePropertyKeys.CorrelationId));
    }

    [Fact]
    public async Task Topic_stops_at_the_first_failure()
    {
        await StartWithRoutes(b => b.From("direct:start").LoadBalance(lb => lb.Topic(), o => o.To("mock:x").To("mock:y").To("mock:z")));
        Fail("y", () => new IOException("y"));
        ExpectNone("z");

        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", 1));

        Assert.IsType<IOException>(ex.InnerException);
        Assert.Equal(1, Mock("x").ReceivedCounter);
        await Mock("z").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Custom_load_balancer_directly_or_from_the_registry()
    {
        Context.Registry.Bind("last", new LastLoadBalancer());
        await StartWithRoutes(b =>
        {
            b.From("direct:direct").LoadBalance(lb => lb.Custom(new LastLoadBalancer()), o => o.To("mock:x").To("mock:y"));
            b.From("direct:ref").LoadBalance(lb => lb.Custom("last"), o => o.To("mock:x").To("mock:y"));
        });
        Mock("y").ExpectedMessageCount(2);
        ExpectNone("x");

        await Template.SendBodyAsync("direct:direct", 1);
        await Template.SendBodyAsync("direct:ref", 2);

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Concurrent_round_robin_is_evenly_distributed()
    {
        await StartWithRoutes(b => b.From("direct:start").LoadBalance(lb => lb.RoundRobin(), o => o.To("mock:x").To("mock:y").To("mock:z")));

        await Task.WhenAll(Enumerable.Range(0, 300).Select(i => Task.Run(() => Template.SendBodyAsync("direct:start", i)))).Bounded();

        Assert.All(new[] { "x", "y", "z" }, m => Assert.Equal(100, Mock(m).ReceivedCounter));
    }

    [Fact]
    public void Load_balancer_classes_validate_their_arguments()
    {
        Assert.Throws<ArgumentException>(() => new WeightedLoadBalancer([0, 0], roundRobin: true));
        Assert.Throws<ArgumentException>(() => new WeightedLoadBalancer([1, -1], roundRobin: true));
        Assert.Throws<ArgumentException>(() => new FailoverLoadBalancer(exceptions: [typeof(string)]));
    }

    private sealed class LastLoadBalancer : QueueLoadBalancer
    {
        protected override int ChooseIndex(Exchange exchange, int count) => count - 1;
    }
}
