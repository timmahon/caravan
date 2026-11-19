using Caravan.Builder;
using Caravan.Model;

namespace Caravan.Dsl.Yaml.Catalog;

/// <summary>Error handling, transactions-like and resilience steps: doTry, onException, onCompletion, throwException, rollback, circuitBreaker, saga.</summary>
internal static class ErrorSteps
{
    private static readonly ObjectSpec Resilience4j = new()
    {
        Options =
        [
            Opt.Num("failureRateThreshold", "Failure rate, in percent, that opens the circuit (→ failureRatio / 100)."),
            Opt.Int("minimumNumberOfCalls", "Calls needed before the rate is computed (→ minimumThroughput)."),
            Opt.Int("slidingWindowSize", "Size of the sliding window, read as seconds (→ samplingDuration; Polly's window is time-based)."),
            Opt.Enum("slidingWindowType", "Accepted; Polly's window is always time-based.", "COUNT_BASED", "TIME_BASED"),
            Opt.Int("waitDurationInOpenState", "Seconds the circuit stays open (→ breakDuration)."),
            Opt.Bool("timeoutEnabled", "Enables the time limit."),
            Opt.Int("timeoutDuration", "The time limit in milliseconds (with timeoutEnabled, default 1000)."),
            Opt.Bool("bulkheadEnabled", "Enables the bulkhead."),
            Opt.Int("bulkheadMaxConcurrentCalls", "Maximum concurrent calls (with bulkheadEnabled)."),
            Opt.Types("recordException", "Only these exceptions count as failures."),
            Opt.Types("ignoreException", "These exceptions do not count as failures."),
            Opt.Ignored("bulkheadMaxWaitDuration", OptionKind.Integer, "Polly's bulkhead has a waiting queue, see circuitBreakerConfiguration.bulkheadMaxWaitingCalls"),
            Opt.Ignored("automaticTransitionFromOpenToHalfOpenEnabled", OptionKind.Boolean, "Polly moves to half-open on the next call"),
            Opt.Ignored("permittedNumberOfCallsInHalfOpenState", OptionKind.Integer, "Polly lets one probe call through"),
            Opt.Ignored("slowCallDurationThreshold", OptionKind.Integer, "slow calls are not tracked"),
            Opt.Ignored("slowCallRateThreshold", OptionKind.Number, "slow calls are not tracked"),
            Opt.Ignored("writableStackTraceEnabled", OptionKind.Boolean, ".NET exceptions always have stack traces"),
            Opt.Ignored("throwExceptionWhenHalfOpenOrOpenState", OptionKind.Boolean, "an open circuit always fails the call"),
            Opt.Ignored("timeoutCancelRunningFuture", OptionKind.Boolean, "the block is always cancelled on timeout"),
            Opt.Ignored("timeoutExecutorService", OptionKind.String, "there is no executor"),
            Opt.Ignored("id", OptionKind.String, "configurations have no id"),
            Opt.Unsupported("circuitBreaker", "configure the circuit inline"),
            Opt.Unsupported("config", "use the configuration option of circuitBreaker"),
        ],
    };

    private static readonly ObjectSpec PollyConfiguration = new()
    {
        Options =
        [
            Opt.Num("failureRatio", "Failure ratio (0..1) that opens the circuit."),
            Opt.Int("minimumThroughput", "Calls needed in the sampling duration before the ratio applies."),
            Opt.Duration("samplingDuration", "The sampling window."),
            Opt.Duration("breakDuration", "How long the circuit stays open."),
            Opt.Duration("timeout", "Time limit of a call."),
            Opt.Int("bulkheadMaxConcurrentCalls", "Maximum concurrent calls."),
            Opt.Int("bulkheadMaxWaitingCalls", "Maximum calls waiting for the bulkhead."),
            Opt.Types("recordExceptions", "Only these exceptions count as failures."),
            Opt.Types("ignoreExceptions", "These exceptions do not count as failures."),
        ],
    };

    private static readonly ObjectSpec SagaEndpoint = new()
    {
        InlineOption = "uri",
        Options = [Opt.Str("uri", "The endpoint URI.", required: true), Opt.Map("parameters", "Endpoint options appended to the URI.")],
    };

    /// <summary>The steps.</summary>
    public static IEnumerable<StepSpec> All =>
    [
        new()
        {
            Name = "doTry",
            Description = "Runs the steps, then the first matching doCatch on failure, then doFinally.",
            Options =
            [
                Opt.StepsOption,
                Opt.Objects("doCatch", "Handles failures of the given exception types.", new ObjectSpec
                {
                    CommonOptions = true,
                    Options =
                    [
                        Opt.Types("exception", "The exception types caught.", required: true),
                        Opt.Predicate("onWhen", "Only catches when this predicate matches."),
                        Opt.StepsOption,
                    ],
                }),
                Opt.Object("doFinally", "Always runs, after the steps and catches.", new ObjectSpec { CommonOptions = true, Options = [Opt.StepsOption] }),
            ],
            Build = o =>
            {
                var tryDefinition = new TryDefinition();
                o.Steps("steps", tryDefinition.Outputs);
                foreach (var clause in o.Objects("doCatch"))
                {
                    CatchDefinition catchDefinition;
                    try
                    {
                        catchDefinition = new CatchDefinition(clause.Types("exception")) { OnWhen = clause.Predicate("onWhen") };
                    }
                    catch (ArgumentException e)
                    {
                        throw clause.Error("exception", e.Message, e);
                    }

                    clause.Steps("steps", catchDefinition.Outputs);
                    clause.ApplyCommon(catchDefinition);
                    tryDefinition.Catches.Add(catchDefinition);
                }

                if (o.Object("doFinally") is { } finallyClause)
                {
                    tryDefinition.Finally = new FinallyDefinition();
                    finallyClause.Steps("steps", tryDefinition.Finally.Outputs);
                    finallyClause.ApplyCommon(tryDefinition.Finally);
                }

                return tryDefinition.Catches.Count == 0 && tryDefinition.Finally is null
                    ? throw o.Context.Error(o.Node, "doTry needs at least one doCatch or a doFinally")
                    : tryDefinition;
            },
        },
        new()
        {
            Name = "onException",
            Description = "Route-scoped exception clause (only among the top-level steps of a route).",
            Options = ModelCatalog.OnException.Options,
            RouteLevelOnly = true,
            Build = ModelCatalog.BuildOnException,
        },
        new()
        {
            Name = "onCompletion",
            Description = "Route-scoped completion block (only among the top-level steps of a route).",
            Options = ModelCatalog.OnCompletion.Options,
            RouteLevelOnly = true,
            Build = o => ModelCatalog.FillOnCompletion(o, new OnCompletionDefinition()),
        },
        new()
        {
            Name = "throwException",
            Description = "Fails the exchange with an exception of the given type and message (or a registry bean).",
            Options =
            [
                Opt.Type("exceptionType", "The exception type (it needs a constructor taking a message)."),
                Opt.Str("message", "The message."),
                Opt.Ref("ref", "An exception"),
            ],
            Build = o =>
            {
                if (o.String("ref") is { } reference)
                {
                    return new ThrowExceptionDefinition(e => e.Context.Registry.Lookup(reference) as Exception ?? throw new NoSuchBeanException(reference));
                }

                var type = o.Type("exceptionType") ?? throw o.Context.Error(o.Node, "throwException needs exceptionType (or ref)");
                return Dsl.Single(s => s.ThrowException(type, o.String("message")));
            },
        },
        new()
        {
            Name = "rollback",
            Description = "Rolls the exchange back (fails it), or only marks it rollback-only.",
            Options =
            [
                Opt.Str("message", "The message of the RollbackExchangeException."),
                Opt.Bool("markRollbackOnly", "Only marks the exchange: routing stops, the unit of work fails."),
                Opt.Bool("markRollbackOnlyLast", "Only marks the current route."),
            ],
            Build = o => new RollbackDefinition
            {
                Message = o.String("message"),
                MarkRollbackOnly = o.Bool("markRollbackOnly") ?? false,
                MarkRollbackOnlyLast = o.Bool("markRollbackOnlyLast") ?? false,
            },
        },
        new()
        {
            Name = "circuitBreaker",
            Description = "Runs the steps behind a circuit breaker (Polly), with an optional fallback.",
            Options =
            [
                Opt.StepsOption,
                Opt.Object("onFallback", "The steps run when the call fails or the circuit is open.", new ObjectSpec
                {
                    CommonOptions = true,
                    Options = [Opt.Bool("fallbackViaNetwork", "Runs the fallback through a circuit of its own."), Opt.StepsOption],
                }),
                Opt.Ref("configuration", "A named circuit breaker configuration").Alias("configurationRef"),
                Opt.Object("resilience4jConfiguration", "Resilience4j options, mapped to Polly.", Resilience4j),
                Opt.Object("circuitBreakerConfiguration", "Caravan: Polly options.", PollyConfiguration),
                Opt.Bool("inheritErrorHandler", "The steps keep the route's error handler."),
                Opt.Unsupported("faultToleranceConfiguration", "use resilience4jConfiguration or circuitBreakerConfiguration"),
            ],
            Build = o =>
            {
                var breaker = new CircuitBreakerDefinition
                {
                    ConfigurationRef = o.String("configuration"),
                    InheritErrorHandler = o.Bool("inheritErrorHandler") ?? false,
                };
                o.Steps("steps", breaker.Outputs);
                if (o.Object("resilience4jConfiguration") is { } r4j)
                {
                    ApplyResilience4j(r4j, breaker.Configuration);
                }

                if (o.Object("circuitBreakerConfiguration") is { } polly)
                {
                    ApplyPolly(polly, breaker.Configuration);
                }

                if (o.Object("onFallback") is { } fallback)
                {
                    breaker.OnFallback = new OnFallbackDefinition { FallbackViaNetwork = fallback.Bool("fallbackViaNetwork") ?? false };
                    fallback.Steps("steps", breaker.OnFallback.Outputs);
                    fallback.ApplyCommon(breaker.OnFallback);
                }

                return breaker;
            },
        },
        new()
        {
            Name = "saga",
            Description = "Runs the steps in a saga with compensation and completion endpoints (without steps: the rest of the route).",
            Options =
            [
                Opt.Enum("propagation", "How the saga relates to the current one.", "Required", "RequiresNew", "Mandatory", "Supports", "NotSupported", "Never"),
                Opt.Enum("completionMode", "Auto: the saga ends with the steps; Manual: with saga:complete or saga:compensate.", "Auto", "Manual"),
                Opt.Object("compensation", "The endpoint called to compensate.", SagaEndpoint),
                Opt.Object("completion", "The endpoint called on completion.", SagaEndpoint),
                Opt.Duration("timeout", "Compensates the saga when not completed in time."),
                Opt.Objects(
                    "option",
                    "Values (expressions) passed as headers to the compensation and completion endpoints.",
                    new ObjectSpec { Expression = ExpressionSlot.Expression, Options = [Opt.Str("key", "The header name.", required: true)] })
                    .Alias("options"),
                Opt.Ref("sagaService", "The ISagaService"),
                Opt.StepsOption,
            ],
            Build = o =>
            {
                var saga = new SagaDefinition
                {
                    Propagation = o.Enum<SagaPropagation>("propagation") ?? SagaPropagation.Required,
                    CompletionMode = o.Enum<SagaCompletionMode>("completionMode") ?? SagaCompletionMode.Auto,
                    CompensationUri = o.Object("compensation") is { } compensation ? EndpointUris.Build(compensation) : null,
                    CompletionUri = o.Object("completion") is { } completion ? EndpointUris.Build(completion) : null,
                    Timeout = o.Duration("timeout"),
                    ScopeIsRestOfRoute = !o.Has("steps"),
                };
                foreach (var option in o.Objects("option"))
                {
                    saga.Options[option.RequiredString("key")] = option.InlineExpression();
                }

                if (o.String("sagaService") is { } service)
                {
                    saga.SagaService = o.Context.Context.Registry.Lookup(service) as Saga.ISagaService
                        ?? throw o.Error("sagaService", $"No ISagaService is bound in the registry as '{service}'");
                }

                return RoutingSteps.Block(o, saga);
            },
        },
    ];

    private static void ApplyResilience4j(YamlObject options, CircuitBreakerConfiguration configuration)
    {
        if (options.Double("failureRateThreshold") is { } rate)
        {
            configuration.FailureRatio = rate / 100;
        }

        configuration.MinimumThroughput = options.Int("minimumNumberOfCalls") ?? configuration.MinimumThroughput;
        if (options.Int("slidingWindowSize") is { } window)
        {
            configuration.SamplingDuration = TimeSpan.FromSeconds(window);
        }

        _ = options.String("slidingWindowType");
        if (options.Int("waitDurationInOpenState") is { } wait)
        {
            configuration.BreakDuration = TimeSpan.FromSeconds(wait);
        }

        if (options.Bool("timeoutEnabled") == true)
        {
            configuration.Timeout = TimeSpan.FromMilliseconds(options.Int("timeoutDuration") ?? 1000);
        }

        if (options.Bool("bulkheadEnabled") == true)
        {
            configuration.BulkheadMaxConcurrentCalls = options.Int("bulkheadMaxConcurrentCalls") ?? 25;
        }

        if (options.Has("recordException"))
        {
            configuration.RecordExceptions = options.Types("recordException");
        }

        if (options.Has("ignoreException"))
        {
            configuration.IgnoreExceptions = options.Types("ignoreException");
        }
    }

    private static void ApplyPolly(YamlObject options, CircuitBreakerConfiguration configuration)
    {
        configuration.FailureRatio = options.Double("failureRatio") ?? configuration.FailureRatio;
        configuration.MinimumThroughput = options.Int("minimumThroughput") ?? configuration.MinimumThroughput;
        configuration.SamplingDuration = options.Duration("samplingDuration") ?? configuration.SamplingDuration;
        configuration.BreakDuration = options.Duration("breakDuration") ?? configuration.BreakDuration;
        configuration.Timeout = options.Duration("timeout") ?? configuration.Timeout;
        configuration.BulkheadMaxConcurrentCalls = options.Int("bulkheadMaxConcurrentCalls") ?? configuration.BulkheadMaxConcurrentCalls;
        configuration.BulkheadMaxWaitingCalls = options.Int("bulkheadMaxWaitingCalls") ?? configuration.BulkheadMaxWaitingCalls;
        if (options.Has("recordExceptions"))
        {
            configuration.RecordExceptions = options.Types("recordExceptions");
        }

        if (options.Has("ignoreExceptions"))
        {
            configuration.IgnoreExceptions = options.Types("ignoreExceptions");
        }
    }
}
