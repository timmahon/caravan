using Caravan.Builder;

namespace Caravan.Core.Tests;

/// <summary>Guards against overload ambiguity: these lambda shapes must keep compiling.</summary>
public class DslOverloadTests : ContextTestBase
{
    [Fact]
    public void Common_lambda_shapes_bind_to_a_single_overload()
    {
        var route = RouteBuilder.Create(b => b.From("direct:start")
            .Filter(_ => true, f => f.To("mock:a"))
            .Filter(e => e.Message.Body is null, _ => { })
            .Filter(_ => throw new InvalidOperationException(), _ => { })
            .Filter((string s) => s.Length > 0, f => f.Stop())
            .Filter((Order o) => o.Total > 0, f => f.Log("big"))
            .Choice(c => c
                .When(_ => false, r => r.To("mock:b"))
                .When((int n) => n > 1, r => r.To("mock:c"))
                .When(Expr.Body().IsNull(), _ => { })
                .Otherwise(r => r.ToD(_ => throw new InvalidOperationException())))
            .SetProperty("p", _ => 1)
            .SetProperty("p", _ => throw new InvalidOperationException())
            .SetProperty("p", (string s) => s.Length)
            .SetHeader("h", (Order o) => o.Id)
            .ToD(e => "mock:" + e.ExchangeId)
            .WireTap(_ => "mock:tap", onPrepare: _ => { })
            .WireTap("mock:tap", copy: false)
            .Pipeline(_ => { })
            .Step("s", _ => { })
            .ConvertBodyTo(typeof(string))
            .ConvertHeaderTo("h", typeof(int))
            .RemoveHeaders("*", "keep")
            .RemoveProperties("*"))
            .Build(Context).Single();

        Assert.Equal(19, route.Outputs.Count);
    }

    [Fact]
    public void Aggregate_and_resequence_lambda_shapes_bind_to_a_single_overload()
    {
        var route = RouteBuilder.Create(b => b.From("direct:start")
            .Aggregate(Expr.Header("id"), AggregationStrategies.GroupedBody(), a => a.CompletionSize(2), then => then.To("mock:a"))
            .Aggregate(
                e => e.Message.GetHeader("id"),
                null,
                a => a
                    .AggregationStrategyRef("s")
                    .CompletionSize(_ => 2)
                    .CompletionSize(Expr.Header("size"))
                    .CompletionTimeout(TimeSpan.FromSeconds(1))
                    .CompletionTimeout(_ => 1000)
                    .CompletionTimeout(Expr.Header("timeout"))
                    .CompletionPredicate(_ => true)
                    .CompletionPredicate(_ => throw new InvalidOperationException())
                    .CompletionPredicate((string s) => s.Length > 3)
                    .CompletionPredicate(Pred.Constant(true))
                    .ParallelProcessing()
                    .OptimisticLocking(),
                _ => { })
            .Aggregate((Order o) => o.Id, AggregationStrategies.UseLatest(), a => a.CompletionInterval(TimeSpan.FromSeconds(1)), then => then.Log("x"))
            .Aggregate(_ => throw new InvalidOperationException(), AggregationStrategies.UseLatest(), a => a.CompletionSize(1), _ => { })
            .Resequence(Expr.Body(), then => then.To("mock:b"))
            .Resequence(e => e.Message.Body, r => r.Stream(capacity: 10), then => then.To("mock:c"))
            .Resequence(_ => 1, r => r.Batch(reverse: true), _ => { })
            .Resequence(_ => throw new InvalidOperationException(), _ => { }))
            .Build(Context).Single();

        Assert.Equal(8, route.Outputs.Count);
    }

    [Fact]
    public void Error_handling_lambda_shapes_bind_to_a_single_overload()
    {
        var routes = RouteBuilder.Create(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead")
                .MaximumRedeliveries(2)
                .RedeliveryDelay(100)
                .RedeliveryDelay(TimeSpan.FromMilliseconds(100))
                .RetryWhile(_ => true)
                .RetryWhile(_ => throw new InvalidOperationException())
                .OnRedelivery(_ => { })
                .OnRedelivery(_ => throw new InvalidOperationException())
                .OnPrepareFailure(e => e.Message.Body = null)
                .OnExceptionOccurred(_ => { }));
            b.OnException<IOException>(o => o
                .Handled(true)
                .Handled(_ => true)
                .Handled(Pred.Constant(true))
                .Continued(_ => false)
                .OnWhen(_ => true)
                .RetryWhile(_ => false)
                .OnRedelivery(_ => { })
                .MaximumRedeliveries(1)
                .To("mock:error"));
            b.OnException([typeof(IOException), typeof(TimeoutException)], o => o.Continued(true));
            b.OnCompletion(c => c.OnWhen(_ => true).OnWhen(Expr.Body().IsNull()).To("mock:done"));
            b.From("direct:start")
                .OnException<InvalidOperationException>(o => o.Handled(e => e.Message.Body is null))
                .OnCompletion(c => c.OnFailureOnly())
                .DoTry(
                    t => t.ThrowException(new IOException()).ThrowException(_ => new IOException()).ThrowException(_ => throw new IOException()),
                    c => c
                        .DoCatch<IOException>(_ => { })
                        .DoCatch<IOException>(_ => { }, onWhen: _ => true)
                        .DoCatch<IOException>(_ => { }, Pred.Constant(true))
                        .DoCatch([typeof(IOException)], _ => { }, onWhen: e => e.Message.Body is null)
                        .DoFinally(f => f.Rollback()))
                .ThrowException(typeof(IOException), "x")
                .MarkRollbackOnly()
                .MarkRollbackOnlyLast()
                .Rollback("m");
        }).Build(Context).Single();

        Assert.Equal(5, routes.Outputs.Count);
        Assert.Equal(3, routes.OnExceptions.Count);
        Assert.Single(routes.OnCompletions);
    }

    [Fact]
    public void Split_multicast_and_recipient_list_lambda_shapes_bind_to_a_single_overload()
    {
        var route = RouteBuilder.Create(b => b.From("direct:start")
            .Split(Expr.Body(), s => s.To("mock:a"))
            .Split(e => e.Message.Body, _ => { })
            .Split(_ => throw new InvalidOperationException(), _ => { })
            .Split((Exchange e) => e.Message.Body, _ => { })
            .Split((string s) => s.Split(','), s => s.Stop(), parallelProcessing: true, streaming: true)
            .Split((Order o) => new[] { o.Id }, s => s.Log("x"), aggregationStrategy: AggregationStrategies.GroupedBody(), stopOnException: true)
            .Split(Expr.Tokenize(","), _ => { }, delimiter: ",", timeout: TimeSpan.FromSeconds(1), onPrepare: _ => { }, shareUnitOfWork: true)
            .Split(Expr.Lines(group: 10, skipFirst: true), _ => { }, aggregationStrategyRef: "s", maxDegreeOfParallelism: 4, parallelAggregate: true)
            .Multicast(m => m.To("mock:a").To("mock:b"))
            .Multicast(_ => { }, parallelProcessing: true, aggregationStrategy: AggregationStrategies.From((old, @new) => old ?? @new))
            .Multicast(_ => { }, aggregationStrategy: AggregationStrategies.From((_, _) => throw new InvalidOperationException()))
            .Multicast(_ => { }, aggregationStrategy: AggregationStrategies.Body((int sum, int n) => sum + n), onPrepare: e => e.Message.Body = null)
            .Multicast(_ => { }, aggregationStrategy: AggregationStrategies.Body((string? all, string s) => all + s))
            .RecipientList(Expr.Header("to"))
            .RecipientList("mock:a,mock:${header.b}", ignoreInvalidEndpoints: true)
            .RecipientList(e => e.Message.GetHeader("to"), delimiter: ";", cacheSize: 10)
            .RecipientList(_ => throw new InvalidOperationException(), parallelProcessing: true)
            .RecipientList(_ => new[] { "mock:a" }))
            .Build(Context).Single();

        Assert.Equal(18, route.Outputs.Count);
    }

    [Fact]
    public void Routing_and_flow_control_lambda_shapes_bind_to_a_single_overload()
    {
        var route = RouteBuilder.Create(b => b.From("direct:start")
            .RoutingSlip(Expr.Header("slip"))
            .RoutingSlip(e => e.Message.GetHeader("slip"), uriDelimiter: "#", ignoreInvalidEndpoints: true, cacheSize: 5)
            .RoutingSlip(_ => throw new InvalidOperationException())
            .DynamicRouter(Expr.Header("next"))
            .DynamicRouter(_ => null)
            .DynamicRouter(_ => throw new InvalidOperationException(), cacheSize: -1)
            .Enrich("direct:a")
            .Enrich("direct:${header.x}", AggregationStrategies.From((o, n) => o ?? n), aggregateOnException: true, shareUnitOfWork: true)
            .Enrich(Expr.Header("uri"), ignoreInvalidEndpoint: true, cacheSize: 1)
            .PollEnrich("seda:a")
            .PollEnrich("seda:a", TimeSpan.FromSeconds(1), AggregationStrategies.UseLatest())
            .PollEnrich(Expr.Header("uri"), timeout: TimeSpan.Zero, aggregationStrategyRef: "s")
            .Loop(3, l => l.To("mock:a"))
            .Loop(Expr.Header("n"), _ => { }, copy: true)
            .Loop(_ => 2, _ => { })
            .Loop(_ => throw new InvalidOperationException(), _ => { }, breakOnShutdown: true)
            .LoopWhile(_ => false, _ => { })
            .LoopWhile(_ => throw new InvalidOperationException(), _ => { })
            .LoopWhile((string s) => s.Length < 3, l => l.Stop())
            .LoopWhile(Pred.Constant(false), _ => { }, copy: true)
            .Delay(TimeSpan.FromSeconds(1))
            .Delay(Expr.Header("delay"), asyncDelayed: false)
            .Delay(_ => 100)
            .Delay(_ => throw new InvalidOperationException())
            .Throttle(5)
            .Throttle(5, t => t.TimePeriod(TimeSpan.FromSeconds(1)).Mode(Model.ThrottlingMode.TotalRequests).RejectExecution().AsyncDelayed(false))
            .Throttle(Expr.Header("max"), t => t.CorrelationExpression(Expr.Header("k")).CallerRunsWhenRejected(false))
            .Throttle(_ => 3, t => t.CorrelationExpression(e => e.Message.GetHeader("k")))
            .Throttle(_ => throw new InvalidOperationException())
            .Sample()
            .Sample(TimeSpan.FromSeconds(5))
            .Sample(messageFrequency: 10)
            .IdempotentConsumer(Expr.Header("id"), new Idempotent.MemoryIdempotentRepository(), i => i.To("mock:a"))
            .IdempotentConsumer(e => e.Message.GetHeader("id"), new Idempotent.MemoryIdempotentRepository(), _ => { }, eager: false, skipDuplicate: false)
            .IdempotentConsumer(_ => throw new InvalidOperationException(), new Idempotent.MemoryIdempotentRepository(), _ => { }, completionEager: true)
            .LoadBalance(lb => lb.RoundRobin(), o => o.To("mock:a").To("mock:b"))
            .LoadBalance(lb => lb.Random(), _ => { })
            .LoadBalance(lb => lb.Topic(), _ => { })
            .LoadBalance(lb => lb.Weighted(true, 1, 2), _ => { })
            .LoadBalance(lb => lb.Weighted(false, "1,2"), _ => { })
            .LoadBalance(lb => lb.Failover(), _ => { })
            .LoadBalance(lb => lb.Failover(typeof(IOException), typeof(TimeoutException)), _ => { })
            .LoadBalance(lb => lb.Failover(3, inheritErrorHandler: false, roundRobin: true, sticky: true, exceptions: [typeof(IOException)]), _ => { })
            .LoadBalance(lb => lb.Sticky(Expr.Header("k")), _ => { })
            .LoadBalance(lb => lb.Sticky(e => e.Message.GetHeader("k")), _ => { })
            .LoadBalance(lb => lb.Sticky(_ => throw new InvalidOperationException()), _ => { })
            .LoadBalance(lb => lb.Custom(new LoadBalancing.RoundRobinLoadBalancer()), _ => { })
            .LoadBalance(lb => lb.Custom("ref"), _ => { })
            .ClaimCheck(Model.ClaimCheckOperation.Set, "k")
            .ClaimCheck(Model.ClaimCheckOperation.Get, "k", filter: "body,-header:x", aggregationStrategy: AggregationStrategies.UseLatest())
            .ClaimCheck(Model.ClaimCheckOperation.Pop, aggregationStrategyRef: "s")
            .Validate(Expr.Body().IsNotNull())
            .Validate(_ => true)
            .Validate(_ => throw new InvalidOperationException())
            .Validate((Order o) => o.Total > 0)
            .Script(Expr.Body())
            .Script(_ => null)
            .Script(_ => throw new InvalidOperationException())
            .SetExchangePattern(ExchangePattern.InOut)
            .Sort()
            .Sort(Expr.Tokenize(","))
            .Sort((string x, string y) => string.CompareOrdinal(x, y))
            .Sort((int x, int y) => x - y, Expr.Header("numbers"))
            .SetHeaders(("a", Expr.Body()), ("b", Expr.Constant(1)))
            .SetHeaders(new Dictionary<string, object?> { ["c"] = 2 }))
            .Build(Context).Single();

        Assert.Equal(65, route.Outputs.Count);
    }

    [Fact]
    public void Circuit_breaker_and_saga_lambda_shapes_bind_to_a_single_overload()
    {
        var route = RouteBuilder.Create(b => b.From("direct:start")
            .CircuitBreaker(cb => cb.To("mock:a"))
            .CircuitBreaker(_ => { }, c => c.FailureRatio(0.5).MinimumThroughput(10).SamplingDuration(TimeSpan.FromSeconds(10)).BreakDuration(TimeSpan.FromSeconds(5)))
            .CircuitBreaker(cb => cb.To("mock:a"), fallback: f => f.Transform(_ => "fallback"))
            .CircuitBreaker(_ => { }, c => c.Timeout(TimeSpan.FromSeconds(1)).Bulkhead(5, 2).ConfigurationRef("shared"), _ => { })
            .CircuitBreaker(cb => cb.Process(_ => throw new InvalidOperationException()), c => c.OnFallback(f => f.Stop()))
            .CircuitBreaker(_ => { }, c => c.OnFallbackViaNetwork(_ => { }).InheritErrorHandler().IgnoreExceptions(typeof(IOException)).RecordExceptions(typeof(Exception)))
            .Saga(s => s.Compensation("direct:undo"))
            .Saga(_ => { })
            .Saga()
            .Saga(s => s.Propagation(Model.SagaPropagation.RequiresNew).Completion("direct:done"), body => body.To("mock:a"))
            .Saga(_ => { }, _ => { })
            .Saga(s => s.Option("id", Expr.Header("id")).Option("n", e => e.Message.Body).Option("x", _ => throw new InvalidOperationException()), _ => { })
            .Saga(s => s.CompletionMode(Model.SagaCompletionMode.Manual).Timeout(TimeSpan.FromMinutes(1)), body => body.SagaComplete())
            .SagaComplete()
            .SagaCompensate())
            .Build(Context).Single();

        Assert.Equal(15, route.Outputs.Count);
    }
}
