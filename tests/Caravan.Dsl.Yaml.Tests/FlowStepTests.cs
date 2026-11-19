using Caravan.Idempotent;
using Caravan.LoadBalancing;
using Caravan.Model;

namespace Caravan.Dsl.Yaml.Tests;

public class FlowStepTests : YamlTestBase
{
    [Fact]
    public async Task Aggregate_by_correlation_with_completion_size_and_predicate()
    {
        Context.Registry.Bind("concat", AggregationStrategies.String(","));
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - aggregate:
                      correlationExpression: { header: group }
                      aggregationStrategy: concat
                      completionSize: 3
                      completionPredicate: { simple: "${exchangeProperty.CaravanAggregatedSize} == 2 && ${header.group} == 'b'" }
                      steps: [ { to: mock:aggregated } ]
            """);
        Mock("aggregated").ExpectedBodiesReceivedInAnyOrder("a1,a2,a3", "b1,end");

        await Template.SendBodyAndHeaderAsync("direct:start", "a1", "group", "a");
        await Template.SendBodyAndHeaderAsync("direct:start", "b1", "group", "b");
        await Template.SendBodyAndHeaderAsync("direct:start", "a2", "group", "a");
        await Template.SendBodyAndHeaderAsync("direct:start", "a3", "group", "a");
        await Template.SendBodyAndHeadersAsync("direct:start", "end", new Dictionary<string, object?> { ["group"] = "b", ["last"] = true });

        await Mock("aggregated").AssertIsSatisfiedAsync();
        var aggregate = Assert.IsType<AggregateDefinition>(Context.RouteDefinitions[0].Outputs[0]);
        Assert.Equal(3, aggregate.CompletionSize);
    }

    [Fact]
    public async Task Aggregate_options_map_to_the_definition()
    {
        Context.Registry.Bind("concat", AggregationStrategies.String(","));
        Context.AddRoutes(YamlRoutesBuilder.FromString("""
            - from:
                uri: direct:start
                steps:
                  - aggregate:
                      correlationExpression: { expression: { constant: all } }
                      strategyRef: concat
                      completionTimeout: 2s
                      completionTimeoutCheckerInterval: 100
                      closeCorrelationKeyOnCompletion: 10
                      eagerCheckCompletion: true
                      discardOnCompletionTimeout: true
                      optimisticLocking: true
                      optimisticLockRetryPolicy: { maximumRetries: 3, retryDelay: 10 }
            """));
        var aggregate = Assert.IsType<AggregateDefinition>(Context.RouteDefinitions[0].Outputs[0]);
        Assert.Equal("concat", aggregate.AggregationStrategyRef);
        Assert.Equal(TimeSpan.FromSeconds(2), aggregate.CompletionTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(100), aggregate.CompletionTimeoutCheckerInterval);
        Assert.Equal(10, aggregate.CloseCorrelationKeyOnCompletion);
        Assert.True(aggregate.EagerCheckCompletion);
        Assert.True(aggregate.DiscardOnCompletionTimeout);
        Assert.Equal(3, aggregate.OptimisticLockRetryPolicy!.MaximumRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(10), aggregate.OptimisticLockRetryPolicy.RetryDelay);
        await Context.StartAsync();
    }

    [Fact]
    public async Task Resequence_batch_orders_by_the_expression()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - resequence:
                      header: seq
                      batchConfig: { batchSize: 3, batchTimeout: 10s }
                      steps: [ { to: mock:ordered } ]
            """);
        Mock("ordered").ExpectedBodiesReceived("a", "b", "c");

        await Template.SendBodyAndHeaderAsync("direct:start", "c", "seq", 3);
        await Template.SendBodyAndHeaderAsync("direct:start", "a", "seq", 1);
        await Template.SendBodyAndHeaderAsync("direct:start", "b", "seq", 2);

        await Mock("ordered").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Resequence_stream_delivers_sequence_numbers_in_order()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - resequence:
                      simple: "${header.seq}"
                      stream-config: { capacity: 10, timeout: 50 }
                      steps: [ { to: mock:ordered } ]
            """);
        Mock("ordered").ExpectedBodiesReceived("1", "2", "3");

        await Template.SendBodyAndHeaderAsync("direct:start", "2", "seq", 2L);
        await Template.SendBodyAndHeaderAsync("direct:start", "1", "seq", 1L);
        await Template.SendBodyAndHeaderAsync("direct:start", "3", "seq", 3L);

        await Mock("ordered").AssertIsSatisfiedAsync();
        Assert.IsType<StreamResequencerConfig>(Assert.IsType<ResequenceDefinition>(Context.RouteDefinitions[0].Outputs[0]).Config);
    }

    [Fact]
    public async Task Loop_by_count_and_while()
    {
        await StartAsync("""
            - from:
                uri: direct:count
                steps:
                  - loop:
                      constant: 3
                      steps: [ { to: mock:count } ]
            - from:
                uri: direct:while
                steps:
                  - loop:
                      simple: "${body.length} < 3"
                      doWhile: true
                      steps:
                        - setBody: { simple: "${body}x" }
                        - to: mock:while
            """);
        Mock("count").ExpectedMessageCount(3);
        Mock("while").ExpectedBodiesReceived("x", "xx", "xxx");

        await Template.SendBodyAsync("direct:count", "c");
        await Template.SendBodyAsync("direct:while", string.Empty);

        await Mock("count").AssertIsSatisfiedAsync();
        await Mock("while").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Delay_throttle_and_sample()
    {
        await StartAsync("""
            - from:
                uri: direct:delay
                steps:
                  - delay: { constant: 10, asyncDelayed: true }
                  - to: mock:delayed
            - from:
                uri: direct:throttle
                steps:
                  - throttle: { constant: 5, timePeriodMillis: 1000, mode: TotalRequests }
                  - to: mock:throttled
            - from:
                uri: direct:sample
                steps:
                  - sample: { messageFrequency: 2 }
                  - to: mock:sampled
            """);
        Mock("delayed").ExpectedMessageCount(1);
        Mock("throttled").ExpectedMessageCount(3);
        Mock("sampled").ExpectedBodiesReceived(2, 4);

        await Template.SendBodyAsync("direct:delay", "x");
        for (var i = 0; i < 3; i++)
        {
            await Template.SendBodyAsync("direct:throttle", i);
        }

        for (var i = 1; i <= 4; i++)
        {
            await Template.SendBodyAsync("direct:sample", i);
        }

        await Mock("delayed").AssertIsSatisfiedAsync();
        await Mock("throttled").AssertIsSatisfiedAsync();
        await Mock("sampled").AssertIsSatisfiedAsync();
        var throttle = Assert.IsType<ThrottleDefinition>(Context.RouteDefinitions[1].Outputs[0]);
        Assert.Equal(ThrottlingMode.TotalRequests, throttle.EffectiveMode);
        Assert.Equal(TimeSpan.FromSeconds(1), throttle.TimePeriod);
    }

    [Fact]
    public async Task IdempotentConsumer_skips_duplicates()
    {
        Context.Registry.Bind("repo", new MemoryIdempotentRepository());
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - idempotentConsumer:
                      header: id
                      idempotentRepository: repo
                      steps: [ { to: mock:unique } ]
            """);
        Mock("unique").ExpectedBodiesReceived("a", "b");

        await Template.SendBodyAndHeaderAsync("direct:start", "a", "id", 1);
        await Template.SendBodyAndHeaderAsync("direct:start", "dup", "id", 1);
        await Template.SendBodyAndHeaderAsync("direct:start", "b", "id", 2);

        await Mock("unique").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task LoadBalance_round_robin_weighted_topic_and_sticky()
    {
        await StartAsync("""
            - from:
                uri: direct:rr
                steps:
                  - loadBalance:
                      roundRobinLoadBalancer: {}
                      steps: [ { to: mock:rr1 }, { to: mock:rr2 } ]
            - from:
                uri: direct:weighted
                steps:
                  - loadBalance:
                      weightedLoadBalancer: { distributionRatio: "2,1", roundRobin: true }
                      steps: [ { to: mock:w1 }, { to: mock:w2 } ]
            - from:
                uri: direct:topic
                steps:
                  - loadBalance:
                      topic: {}
                      steps: [ { to: mock:t1 }, { to: mock:t2 } ]
            - from:
                uri: direct:sticky
                steps:
                  - loadBalance:
                      stickyLoadBalancer: { correlationExpression: { header: key } }
                      steps: [ { to: mock:s1 }, { to: mock:s2 } ]
            - from:
                uri: direct:random
                steps:
                  - loadBalance:
                      random: {}
                      steps: [ { to: mock:r } ]
            """);
        Mock("rr1").ExpectedBodiesReceived(1, 3);
        Mock("rr2").ExpectedBodiesReceived(2);
        Mock("w1").ExpectedBodiesReceived(1, 3);
        Mock("w2").ExpectedBodiesReceived(2);
        Mock("t1").ExpectedBodiesReceived("t");
        Mock("t2").ExpectedBodiesReceived("t");
        Mock("r").ExpectedMessageCount(2);

        for (var i = 1; i <= 3; i++)
        {
            await Template.SendBodyAsync("direct:rr", i);
            await Template.SendBodyAsync("direct:weighted", i);
        }

        await Template.SendBodyAsync("direct:topic", "t");
        await Template.SendBodyAndHeaderAsync("direct:sticky", "k1", "key", "same");
        await Template.SendBodyAndHeaderAsync("direct:sticky", "k2", "key", "same");
        await Template.SendBodyAsync("direct:random", 1);
        await Template.SendBodyAsync("direct:random", 2);

        await MockEndpointAssertions();
        var s1 = Mock("s1").ReceivedExchanges.Count;
        var s2 = Mock("s2").ReceivedExchanges.Count;
        Assert.True((s1, s2) is (2, 0) or (0, 2));
    }

    [Fact]
    public async Task LoadBalance_failover_and_custom()
    {
        Context.Registry.Bind("custom", new RoundRobinLoadBalancer());
        await StartAsync("""
            - from:
                uri: direct:failover
                steps:
                  - loadBalance:
                      failoverLoadBalancer:
                        exception: [ java.lang.IllegalStateException ]
                        maximumFailoverAttempts: 2
                      steps:
                        - pipeline:
                            steps:
                              - to: mock:first
                              - throwException: { exceptionType: java.lang.IllegalStateException, message: down }
                        - to: mock:second
            - from:
                uri: direct:custom
                steps:
                  - loadBalance:
                      customLoadBalancer: { ref: custom }
                      steps: [ { to: mock:c1 }, { to: mock:c2 } ]
            """);
        Mock("first").ExpectedMessageCount(1);
        Mock("second").ExpectedBodiesReceived("x");
        Mock("c1").ExpectedBodiesReceived(1);
        Mock("c2").ExpectedBodiesReceived(2);

        await Template.SendBodyAsync("direct:failover", "x");
        await Template.SendBodyAsync("direct:custom", 1);
        await Template.SendBodyAsync("direct:custom", 2);

        await MockEndpointAssertions();
        var failover = Assert.IsType<FailoverLoadBalancerDefinition>(
            Assert.IsType<LoadBalanceDefinition>(Context.RouteDefinitions[0].Outputs[0]).LoadBalancer);
        Assert.Equal([typeof(InvalidOperationException)], failover.Exceptions);
        Assert.Equal(2, failover.MaximumFailoverAttempts);
    }

    private Task MockEndpointAssertions() => Components.Mock.MockEndpoint.AssertIsSatisfiedAsync(Context);
}
