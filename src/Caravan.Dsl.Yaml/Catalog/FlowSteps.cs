using Caravan.Aggregation;
using Caravan.Model;

namespace Caravan.Dsl.Yaml.Catalog;

/// <summary>Stateful and flow-control steps: aggregator, resequencer, loop, delay, throttle, sample, idempotent consumer, load balancer.</summary>
internal static class FlowSteps
{
    private static readonly ObjectSpec BatchConfig = new()
    {
        Options =
        [
            Opt.Int("batchSize", "Exchanges per batch (default 100)."),
            Opt.Duration("batchTimeout", "Emits a batch this long after its first exchange (default 1s)."),
            Opt.Bool("allowDuplicates", "Keeps exchanges with equal keys."),
            Opt.Bool("reverse", "Emits in descending order."),
            Opt.Bool("ignoreInvalidExchanges", "Ignores exchanges whose key cannot be evaluated."),
        ],
    };

    private static readonly ObjectSpec StreamConfig = new()
    {
        Options =
        [
            Opt.Int("capacity", "Maximum number of exchanges held (default 1000)."),
            Opt.Duration("timeout", "How long to wait for a missing sequence number (default 1s)."),
            Opt.Duration("deliveryAttemptInterval", "Bound of the delivery loop's sleep (default 1s)."),
            Opt.Bool("ignoreInvalidExchanges", "Ignores exchanges whose key cannot be evaluated."),
            Opt.Bool("rejectOld", "Fails exchanges older than the last delivered one."),
            Opt.Unsupported("comparator", "sequence numbers are compared as numbers"),
        ],
    };

    private static readonly ObjectSpec RetryPolicySpec = new()
    {
        Options =
        [
            Opt.Int("maximumRetries", "Maximum retries (0: unbounded)."),
            Opt.Duration("retryDelay", "Delay before a retry."),
            Opt.Duration("maximumRetryDelay", "Upper bound of the delay."),
            Opt.Bool("exponentialBackOff", "Doubles the delay after each retry (default true)."),
            Opt.Bool("randomBackOff", "Randomizes the delay."),
        ],
    };

    private static readonly ObjectSpec Empty = new() { Options = [Opt.Ignored("id", OptionKind.String, "load balancers have no id")] };

    /// <summary>The steps.</summary>
    public static IEnumerable<StepSpec> All =>
    [
        new()
        {
            Name = "aggregate",
            Description = "Combines correlated exchanges into one, sent through the steps when a group completes.",
            Options =
            [
                Opt.Expression("correlationExpression", "The correlation key.", required: true),
                Opt.Ref("aggregationStrategy", "The aggregation strategy").Alias("strategyRef", "aggregationStrategyRef").Req(),
                Opt.Ref("aggregationRepository", "The aggregation repository").Alias("aggregationRepositoryRef"),
                Opt.Int("completionSize", "Completes a group of this many exchanges."),
                Opt.Expression("completionSizeExpression", "Completion size per exchange."),
                Opt.Duration("completionTimeout", "Completes a group inactive for this long."),
                Opt.Expression("completionTimeoutExpression", "Completion timeout per exchange (milliseconds)."),
                Opt.Duration("completionTimeoutCheckerInterval", "How often timeouts are checked (default 1s)."),
                Opt.Duration("completionInterval", "Completes all groups periodically."),
                Opt.Predicate("completionPredicate", "Completes a group when this matches."),
                Opt.Bool("completionFromBatchConsumer", "Completes with the batch of a batch consumer."),
                Opt.Bool("completionOnNewCorrelationGroup", "Completes the other groups when a new group starts."),
                Opt.Bool("eagerCheckCompletion", "Checks completion on the incoming exchange."),
                Opt.Bool("forceCompletionOnStop", "Completes all groups when the route stops."),
                Opt.Bool("completeAllOnStop", "Waits for all groups to complete when the route stops."),
                Opt.Bool("ignoreInvalidCorrelationKeys", "Ignores exchanges without a correlation key."),
                Opt.Int("closeCorrelationKeyOnCompletion", "Rejects late exchanges of completed groups (keeps this many keys; 0 all)."),
                Opt.Bool("discardOnCompletionTimeout", "Discards groups completed by timeout."),
                Opt.Bool("discardOnAggregationFailure", "Discards a group when the strategy fails."),
                Opt.Bool("parallelProcessing", "Sends completed groups concurrently."),
                Opt.Int("maxDegreeOfParallelism", "Caravan: maximum completed groups sent at once with parallelProcessing (default 10)."),
                Opt.Bool("optimisticLocking", "Uses optimistic locking instead of a lock."),
                Opt.Object("optimisticLockRetryPolicy", "Retries of optimistic locking.", RetryPolicySpec),
                Opt.Ref("aggregateController", "An IAggregateController"),
                Opt.StepsOption,
                Opt.Ignored("executorService", OptionKind.String, "groups are sent on the thread pool"),
                Opt.Ignored("timeoutCheckerExecutorService", OptionKind.String, "timeouts are checked by a background task"),
                Opt.Unsupported("aggregationStrategyMethodName", "use a strategy implementing IAggregationStrategy"),
                Opt.Unsupported("aggregationStrategyMethodAllowNull", "use a strategy implementing IAggregationStrategy"),
            ],
            Build = o =>
            {
                var aggregate = new AggregateDefinition(o.Expression("correlationExpression")!)
                {
                    AggregationStrategyRef = o.String("aggregationStrategy"),
                    AggregationRepositoryRef = o.String("aggregationRepository"),
                    CompletionSize = o.Int("completionSize") ?? 0,
                    CompletionSizeExpression = o.Expression("completionSizeExpression"),
                    CompletionTimeout = o.Duration("completionTimeout"),
                    CompletionTimeoutExpression = o.Expression("completionTimeoutExpression"),
                    CompletionTimeoutCheckerInterval = o.Duration("completionTimeoutCheckerInterval"),
                    CompletionInterval = o.Duration("completionInterval"),
                    CompletionPredicate = o.Predicate("completionPredicate"),
                    CompletionFromBatchConsumer = o.Bool("completionFromBatchConsumer") ?? false,
                    CompletionOnNewCorrelationGroup = o.Bool("completionOnNewCorrelationGroup") ?? false,
                    EagerCheckCompletion = o.Bool("eagerCheckCompletion") ?? false,
                    ForceCompletionOnStop = o.Bool("forceCompletionOnStop") ?? false,
                    CompleteAllOnStop = o.Bool("completeAllOnStop") ?? false,
                    IgnoreInvalidCorrelationKeys = o.Bool("ignoreInvalidCorrelationKeys") ?? false,
                    CloseCorrelationKeyOnCompletion = o.Int("closeCorrelationKeyOnCompletion"),
                    DiscardOnCompletionTimeout = o.Bool("discardOnCompletionTimeout") ?? false,
                    DiscardOnAggregationFailure = o.Bool("discardOnAggregationFailure") ?? false,
                    ParallelProcessing = o.Bool("parallelProcessing") ?? false,
                    MaxDegreeOfParallelism = o.Int("maxDegreeOfParallelism") ?? 0,
                    OptimisticLocking = o.Bool("optimisticLocking") ?? false,
                };
                if (o.Object("optimisticLockRetryPolicy") is { } retry)
                {
                    var policy = new OptimisticLockRetryPolicy();
                    policy.MaximumRetries = retry.Int("maximumRetries") ?? policy.MaximumRetries;
                    policy.RetryDelay = retry.Duration("retryDelay") ?? policy.RetryDelay;
                    policy.MaximumRetryDelay = retry.Duration("maximumRetryDelay") ?? policy.MaximumRetryDelay;
                    policy.ExponentialBackOff = retry.Bool("exponentialBackOff") ?? policy.ExponentialBackOff;
                    policy.RandomBackOff = retry.Bool("randomBackOff") ?? policy.RandomBackOff;
                    aggregate.OptimisticLockRetryPolicy = policy;
                }

                if (o.String("aggregateController") is { } controller)
                {
                    aggregate.AggregateController = o.Context.Context.Registry.Lookup(controller) as Spi.IAggregateController
                        ?? throw o.Error("aggregateController", $"No IAggregateController is bound in the registry as '{controller}'");
                }

                return RoutingSteps.Block(o, aggregate);
            },
        },
        new()
        {
            Name = "resequence",
            Description = "Re-orders exchanges by the expression (batch or stream resequencer) before the steps.",
            Expression = ExpressionSlot.Expression,
            Options =
            [
                Opt.Object("batchConfig", "Batch resequencer (the default).", BatchConfig),
                Opt.Object("streamConfig", "Stream resequencer (long sequence numbers).", StreamConfig),
                Opt.StepsOption,
            ],
            Build = o =>
            {
                var resequence = new ResequenceDefinition(o.InlineExpression());
                if (o.Has("batchConfig") && o.Has("streamConfig"))
                {
                    throw o.Context.Error(o.Node, "resequence takes batchConfig or streamConfig, not both");
                }

                if (o.Object("streamConfig") is { } stream)
                {
                    var config = new StreamResequencerConfig();
                    config.Capacity = stream.Int("capacity") ?? config.Capacity;
                    config.Timeout = stream.Duration("timeout") ?? config.Timeout;
                    config.DeliveryAttemptInterval = stream.Duration("deliveryAttemptInterval") ?? config.DeliveryAttemptInterval;
                    config.IgnoreInvalidExchanges = stream.Bool("ignoreInvalidExchanges") ?? false;
                    config.RejectOld = stream.Bool("rejectOld") ?? false;
                    resequence.Config = config;
                }
                else if (o.Object("batchConfig") is { } batch)
                {
                    var config = new BatchResequencerConfig();
                    config.BatchSize = batch.Int("batchSize") ?? config.BatchSize;
                    config.BatchTimeout = batch.Duration("batchTimeout") ?? config.BatchTimeout;
                    config.AllowDuplicates = batch.Bool("allowDuplicates") ?? false;
                    config.Reverse = batch.Bool("reverse") ?? false;
                    config.IgnoreInvalidExchanges = batch.Bool("ignoreInvalidExchanges") ?? false;
                    resequence.Config = config;
                }

                return RoutingSteps.Block(o, resequence);
            },
        },
        new()
        {
            Name = "loop",
            Description = "Runs the steps a number of times (the expression) or while the expression (a predicate, with doWhile) matches.",
            Expression = ExpressionSlot.Expression,
            Options =
            [
                Opt.Bool("doWhile", "The expression is a predicate checked before each iteration."),
                Opt.Bool("copy", "Each iteration starts from a copy of the input."),
                Opt.Bool("breakOnShutdown", "Stops looping when the context shuts down."),
                Opt.StepsOption,
                Opt.Unsupported("onPrepare", "loop has no onPrepare"),
            ],
            Build = o =>
            {
                var loop = new LoopDefinition { Copy = o.Bool("copy") ?? false, BreakOnShutdown = o.Bool("breakOnShutdown") ?? false };
                if (o.Bool("doWhile") == true)
                {
                    loop.While = o.InlinePredicate();
                }
                else
                {
                    loop.Count = o.InlineExpression();
                }

                return RoutingSteps.Block(o, loop);
            },
        },
        new()
        {
            Name = "delay",
            Description = "Delays the exchange by the expression's value (milliseconds or a TimeSpan).",
            Expression = ExpressionSlot.Expression,
            Options =
            [
                Opt.Bool("asyncDelayed", "Waits without blocking a thread (default true)."),
                Opt.Bool("callerRunsWhenRejected", "Accepted; there is no executor."),
                Opt.Ignored("executorService", OptionKind.String, "there is no executor"),
            ],
            Build = o => new DelayDefinition(o.InlineExpression())
            {
                AsyncDelayed = o.Bool("asyncDelayed") ?? true,
                CallerRunsWhenRejected = o.Bool("callerRunsWhenRejected") ?? true,
            },
        },
        new()
        {
            Name = "throttle",
            Description = "Limits the exchanges: concurrent requests (default) or, with timePeriodMillis, requests per period (the expression's value).",
            Expression = ExpressionSlot.Expression,
            Options =
            [
                Opt.Duration("timePeriodMillis", "The period of the TotalRequests mode.").Alias("timePeriod"),
                Opt.Enum("mode", "The throttling mode.", "ConcurrentRequests", "TotalRequests"),
                Opt.Expression("correlationExpression", "Throttles per value of this expression."),
                Opt.Bool("rejectExecution", "Fails exchanges over the limit instead of waiting."),
                Opt.Bool("asyncDelayed", "Waits without blocking a thread (default true)."),
                Opt.Bool("callerRunsWhenRejected", "Accepted; there is no executor."),
                Opt.Ignored("executorService", OptionKind.String, "there is no executor"),
            ],
            Build = o => new ThrottleDefinition(o.InlineExpression())
            {
                TimePeriod = o.Duration("timePeriodMillis"),
                Mode = o.Enum<ThrottlingMode>("mode"),
                CorrelationExpression = o.Expression("correlationExpression"),
                RejectExecution = o.Bool("rejectExecution") ?? false,
                AsyncDelayed = o.Bool("asyncDelayed") ?? true,
                CallerRunsWhenRejected = o.Bool("callerRunsWhenRejected") ?? true,
            },
        },
        new()
        {
            Name = "sample",
            Description = "Lets one exchange per period (or every Nth) through and stops the others.",
            Options =
            [
                Opt.Duration("samplePeriod", "The period (default 1s)."),
                Opt.Int("messageFrequency", "Lets every Nth exchange through instead."),
            ],
            Build = o =>
            {
                var sample = new SampleDefinition { MessageFrequency = o.Long("messageFrequency") };
                sample.SamplePeriod = o.Duration("samplePeriod") ?? sample.SamplePeriod;
                return sample;
            },
        },
        new()
        {
            Name = "idempotentConsumer",
            Description = "Runs the steps once per message id (the expression), using an idempotent repository.",
            Expression = ExpressionSlot.Expression,
            Options =
            [
                Opt.Ref("idempotentRepository", "The IIdempotentRepository", required: true),
                Opt.Bool("eager", "Adds the id on arrival (default true)."),
                Opt.Bool("completionEager", "Confirms the id right after the steps."),
                Opt.Bool("skipDuplicate", "Skips duplicates (default true)."),
                Opt.Bool("removeOnFailure", "Removes the id when the exchange fails (default true)."),
                Opt.StepsOption,
            ],
            Build = o => RoutingSteps.Block(o, new IdempotentConsumerDefinition(o.InlineExpression())
            {
                RepositoryRef = o.String("idempotentRepository"),
                Eager = o.Bool("eager") ?? true,
                CompletionEager = o.Bool("completionEager") ?? false,
                SkipDuplicate = o.Bool("skipDuplicate") ?? true,
                RemoveOnFailure = o.Bool("removeOnFailure") ?? true,
            }),
        },
        new()
        {
            Name = "loadBalance",
            Description = "Sends each exchange to one (or, with topic, all) of the steps, chosen by a load balancer.",
            Options =
            [
                Opt.Object("roundRobinLoadBalancer", "Round robin.", Empty).Alias("roundRobin"),
                Opt.Object("randomLoadBalancer", "Random.", Empty).Alias("random"),
                Opt.Object("weightedLoadBalancer", "Weighted round robin or random.", new ObjectSpec
                {
                    Options =
                    [
                        Opt.Strings("distributionRatio", "One weight per step, e.g. \"4,2,1\".").Req(),
                        Opt.Str("distributionRatioDelimiter", "The delimiter of distributionRatio (default ,)."),
                        Opt.Bool("roundRobin", "Round robin within each cycle (default random)."),
                        Opt.Ignored("id", OptionKind.String, "load balancers have no id"),
                    ],
                }).Alias("weighted"),
                Opt.Object("failoverLoadBalancer", "Tries the next step when one fails.", new ObjectSpec
                {
                    Options =
                    [
                        Opt.Types("exception", "Only fails over for these exceptions (default all)."),
                        Opt.Int("maximumFailoverAttempts", "Maximum attempts (-1 unbounded)."),
                        Opt.Bool("roundRobin", "Starts at the next step each time."),
                        Opt.Bool("sticky", "Starts at the last good step."),
                        Opt.Bool("inheritErrorHandler", "Steps keep the route's error handler (default true)."),
                        Opt.Ignored("id", OptionKind.String, "load balancers have no id"),
                    ],
                }).Alias("failover"),
                Opt.Object("stickyLoadBalancer", "Same correlation value, same step.", new ObjectSpec
                {
                    Options =
                    [
                        Opt.Expression("correlationExpression", "The correlation value.", required: true),
                        Opt.Ignored("id", OptionKind.String, "load balancers have no id"),
                    ],
                }).Alias("sticky"),
                Opt.Object("topicLoadBalancer", "Sends to all steps.", Empty).Alias("topic"),
                Opt.Object("customLoadBalancer", "An ILoadBalancer bound in the registry.", new ObjectSpec
                {
                    InlineOption = "ref",
                    Options = [Opt.Ref("ref", "The load balancer", required: true), Opt.Ignored("id", OptionKind.String, "load balancers have no id")],
                }).Alias("custom"),
                Opt.Ignored("inheritErrorHandler", OptionKind.Boolean, "set it on failoverLoadBalancer"),
                Opt.StepsOption,
            ],
            Build = o =>
            {
                var balancers = o.Names.Where(n => n is not ("steps" or "inheritErrorHandler" or "id" or "description" or "disabled")).ToList();
                if (balancers.Count != 1)
                {
                    throw o.Context.Error(o.Node, "loadBalance needs exactly one load balancer (roundRobinLoadBalancer, randomLoadBalancer, weightedLoadBalancer, failoverLoadBalancer, stickyLoadBalancer, topicLoadBalancer, customLoadBalancer)");
                }

                var balancer = o.Object(balancers[0])!;
                LoadBalancerDefinition definition = balancers[0] switch
                {
                    "roundRobinLoadBalancer" => new RoundRobinLoadBalancerDefinition(),
                    "randomLoadBalancer" => new RandomLoadBalancerDefinition(),
                    "topicLoadBalancer" => new TopicLoadBalancerDefinition(),
                    "stickyLoadBalancer" => new StickyLoadBalancerDefinition(balancer.Expression("correlationExpression")!),
                    "customLoadBalancer" => new CustomLoadBalancerDefinition { Ref = balancer.RequiredString("ref") },
                    "weightedLoadBalancer" => Weighted(balancer),
                    _ => Failover(balancer),
                };
                return RoutingSteps.Block(o, new LoadBalanceDefinition { LoadBalancer = definition });
            },
        },
    ];

    private static WeightedLoadBalancerDefinition Weighted(YamlObject options)
    {
        var delimiter = options.String("distributionRatioDelimiter") ?? ",";
        var weighted = new WeightedLoadBalancerDefinition { RoundRobin = options.Bool("roundRobin") ?? false };
        foreach (var part in options.Strings("distributionRatio").SelectMany(s => s.Split(delimiter, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)))
        {
            weighted.DistributionRatio.Add(int.TryParse(part, System.Globalization.CultureInfo.InvariantCulture, out var weight)
                ? weight
                : throw options.Error("distributionRatio", $"distributionRatio must be integers, but found '{part}'"));
        }

        return weighted;
    }

    private static FailoverLoadBalancerDefinition Failover(YamlObject options)
    {
        var failover = new FailoverLoadBalancerDefinition
        {
            RoundRobin = options.Bool("roundRobin") ?? false,
            Sticky = options.Bool("sticky") ?? false,
            InheritErrorHandlerEnabled = options.Bool("inheritErrorHandler") ?? true,
        };
        failover.MaximumFailoverAttempts = options.Int("maximumFailoverAttempts") ?? failover.MaximumFailoverAttempts;
        failover.Exceptions.AddRange(options.Types("exception"));
        return failover;
    }
}
