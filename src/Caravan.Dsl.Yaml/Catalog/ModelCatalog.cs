using Caravan.Model;

namespace Caravan.Dsl.Yaml.Catalog;

/// <summary>
/// Objects of the YAML DSL shared by steps, routes, route configurations and the top level: redelivery policies, error handlers,
/// <c>onException</c>, <c>onCompletion</c> and the interceptors.
/// </summary>
internal static class ModelCatalog
{
    /// <summary>Camel's redelivery policy options.</summary>
    public static readonly ObjectSpec RedeliveryPolicy = new()
    {
        Options =
        [
            Opt.Int("maximumRedeliveries", "Maximum number of redeliveries (0 none, -1 forever)."),
            Opt.Duration("redeliveryDelay", "Delay before the first redelivery."),
            Opt.Duration("maximumRedeliveryDelay", "Upper bound of the delay with exponential back-off."),
            Opt.Bool("useExponentialBackOff", "Multiplies the delay by backOffMultiplier after each attempt."),
            Opt.Num("backOffMultiplier", "The back-off multiplier."),
            Opt.Bool("useCollisionAvoidance", "Randomizes the delay by collisionAvoidanceFactor."),
            Opt.Num("collisionAvoidanceFactor", "The collision avoidance factor."),
            Opt.Str("delayPattern", "Delays per attempt range, e.g. 1:1000;5:5000."),
            Opt.Bool("asyncDelayedRedelivery", "Accepted; redelivery delays are always non-blocking."),
            Opt.Bool("logRetryAttempted", "Logs each redelivery attempt."),
            Opt.Enum("retryAttemptedLogLevel", "Level of the redelivery attempt log.", "TRACE", "DEBUG", "INFO", "WARN", "ERROR", "OFF"),
            Opt.Enum("retriesExhaustedLogLevel", "Level of the exhausted log.", "TRACE", "DEBUG", "INFO", "WARN", "ERROR", "OFF"),
            Opt.Bool("logExhausted", "Logs exhausted failures."),
            Opt.Bool("logHandled", "Logs handled failures."),
            Opt.Bool("logContinued", "Logs continued failures."),
            Opt.Bool("logStackTrace", "Logs the stack trace of exhausted failures."),
            Opt.Bool("logRetryStackTrace", "Logs the stack trace of redelivery attempts."),
            Opt.Bool("logNewException", "Logs a new exception thrown by the failure processor."),
            Opt.Bool("logExhaustedMessageHistory", "Logs the message history of exhausted failures."),
            Opt.Bool("logExhaustedMessageBody", "Logs the exchange (body and headers) of exhausted failures."),
            Opt.Bool("disableRedelivery", "Disables redelivery (maximumRedeliveries 0)."),
            Opt.Ignored("allowRedeliveryWhileStopping", OptionKind.Boolean, "redelivery always stops with the context"),
            Opt.Ignored("retryAttemptedLogInterval", OptionKind.Integer, "every attempt is logged"),
            Opt.Ignored("exchangeFormatterRef", OptionKind.String, "the built-in exchange formatter is used"),
            Opt.Ignored("id", OptionKind.String, "redelivery policies have no id"),
        ],
    };

    /// <summary>Options shared by the default error handler and the dead letter channel.</summary>
    private static readonly OptionSpec[] RedeliveryErrorHandlerOptions =
    [
        Opt.Object("redeliveryPolicy", "The redelivery policy.", RedeliveryPolicy),
        Opt.Bool("useOriginalMessage", "The failure processor gets the message as it entered the route."),
        Opt.Bool("useOriginalBody", "The failure processor gets the body as it entered the route."),
        Opt.Ref("onRedeliveryRef", "A processor called before each redelivery"),
        Opt.Ref("onExceptionOccurredRef", "A processor called when an exception occurs"),
        Opt.Ref("onPrepareFailureRef", "A processor called before the failure processor"),
        Opt.Ref("retryWhileRef", "A predicate deciding whether to keep redelivering"),
        Opt.Str("logName", "The logger name."),
        Opt.Ignored("level", OptionKind.String, "use the redelivery policy's log levels"),
        Opt.Ignored("executorServiceRef", OptionKind.String, "redelivery delays are asynchronous"),
        Opt.Ignored("id", OptionKind.String, "error handlers have no id"),
        Opt.Unsupported("redeliveryPolicyRef", "configure redeliveryPolicy inline"),
        Opt.Unsupported("loggerRef", "use logName"),
    ];

    /// <summary>The dead letter channel.</summary>
    public static readonly ObjectSpec DeadLetterChannel = new()
    {
        InlineOption = "deadLetterUri",
        Options =
        [
            Opt.Str("deadLetterUri", "The endpoint exhausted failures are sent to.", required: true),
            Opt.Bool("deadLetterHandleNewException", "Whether a new exception thrown while sending to the dead letter endpoint is handled (default true)."),
            .. RedeliveryErrorHandlerOptions,
        ],
    };

    /// <summary>Camel's <c>errorHandler</c>: one of the error handler kinds.</summary>
    public static readonly ObjectSpec ErrorHandler = new()
    {
        Options =
        [
            Opt.Object("deadLetterChannel", "Sends exhausted failures to an endpoint.", DeadLetterChannel),
            Opt.Object("defaultErrorHandler", "Logs exhausted failures, which propagate.", new ObjectSpec { Options = RedeliveryErrorHandlerOptions }),
            Opt.Object("noErrorHandler", "No error handling.", new ObjectSpec { Options = [Opt.Ignored("id", OptionKind.String, "error handlers have no id")] }),
            Opt.Object(
                "refErrorHandler",
                "An error handler definition bound in the registry.",
                new ObjectSpec { InlineOption = "ref", Options = [Opt.Ref("ref", "The error handler definition", required: true), Opt.Ignored("id", OptionKind.String, "error handlers have no id")] }),
            Opt.Unsupported("jtaTransactionErrorHandler", "transactions are not supported"),
            Opt.Unsupported("springTransactionErrorHandler", "transactions are not supported"),
        ],
    };

    /// <summary>Camel's <c>onException</c>.</summary>
    public static readonly ObjectSpec OnException = new()
    {
        CommonOptions = true,
        Options =
        [
            Opt.Types("exception", "The exception types handled (.NET names; common Java names are mapped).", required: true),
            Opt.Predicate("onWhen", "Only handles the exception when this predicate matches."),
            Opt.Predicate("handled", "Whether the exception is handled (routing stops, the caller sees success)."),
            Opt.Predicate("continued", "Whether the exception is ignored and routing continues."),
            Opt.Predicate("retryWhile", "Keeps redelivering while this predicate matches."),
            Opt.Object("redeliveryPolicy", "Overrides the error handler's redelivery policy.", RedeliveryPolicy),
            Opt.Bool("useOriginalMessage", "The block gets the message as it entered the route."),
            Opt.Bool("useOriginalBody", "The block gets the body as it entered the route."),
            Opt.Ref("onRedeliveryRef", "A processor called before each redelivery"),
            Opt.Ref("onExceptionOccurredRef", "A processor called when the exception occurs"),
            Opt.StepsOption,
            Opt.Unsupported("redeliveryPolicyRef", "configure redeliveryPolicy inline"),
        ],
    };

    /// <summary>Camel's <c>onCompletion</c>.</summary>
    public static readonly ObjectSpec OnCompletion = new()
    {
        CommonOptions = true,
        Options =
        [
            Opt.Enum("mode", "When the block runs.", "AfterConsumer", "BeforeConsumer"),
            Opt.Bool("onCompleteOnly", "Only when the exchange completed successfully."),
            Opt.Bool("onFailureOnly", "Only when the exchange failed."),
            Opt.Predicate("onWhen", "Only when this predicate matches."),
            Opt.Bool("parallelProcessing", "Runs the block in the background."),
            Opt.Bool("useOriginalMessage", "The block gets the message as it entered the route."),
            Opt.StepsOption,
            Opt.Ignored("executorService", OptionKind.String, "there are no executor services"),
        ],
    };

    /// <summary>Camel's <c>intercept</c>.</summary>
    public static readonly ObjectSpec Intercept = new()
    {
        CommonOptions = true,
        Options = [Opt.Predicate("onWhen", "Only intercepts when this predicate matches."), Opt.StepsOption],
    };

    /// <summary>Camel's <c>interceptFrom</c>.</summary>
    public static readonly ObjectSpec InterceptFrom = new()
    {
        CommonOptions = true,
        Options =
        [
            Opt.Str("uri", "The endpoint pattern of the routes intercepted (exact, wildcard or regular expression); all routes when absent."),
            Opt.Predicate("onWhen", "Only intercepts when this predicate matches."),
            Opt.StepsOption,
        ],
    };

    /// <summary>Camel's <c>interceptSendToEndpoint</c>.</summary>
    public static readonly ObjectSpec InterceptSendToEndpoint = new()
    {
        CommonOptions = true,
        InlineOption = "uri",
        Options =
        [
            Opt.Str("uri", "The endpoint pattern intercepted (exact, wildcard or regular expression).", required: true),
            Opt.Bool("skipSendToOriginalEndpoint", "Skips the original endpoint when the block ran."),
            Opt.Str("afterUri", "An endpoint called after the original endpoint."),
            Opt.Predicate("onWhen", "Only intercepts when this predicate matches."),
            Opt.StepsOption,
        ],
    };

    /// <summary>Applies the redelivery policy options of <paramref name="options"/> (if any) to <paramref name="policy"/>.</summary>
    public static void ApplyRedeliveryPolicy(YamlObject? options, RedeliveryPolicyDefinition policy)
    {
        if (options is null)
        {
            return;
        }

        policy.MaximumRedeliveries = options.Bool("disableRedelivery") == true ? 0 : options.Int("maximumRedeliveries") ?? policy.MaximumRedeliveries;
        policy.RedeliveryDelay = options.Duration("redeliveryDelay") ?? policy.RedeliveryDelay;
        policy.MaximumRedeliveryDelay = options.Duration("maximumRedeliveryDelay") ?? policy.MaximumRedeliveryDelay;
        policy.UseExponentialBackOff = options.Bool("useExponentialBackOff") ?? policy.UseExponentialBackOff;
        policy.BackOffMultiplier = options.Double("backOffMultiplier") ?? policy.BackOffMultiplier;
        policy.UseCollisionAvoidance = options.Bool("useCollisionAvoidance") ?? policy.UseCollisionAvoidance;
        policy.CollisionAvoidanceFactor = options.Double("collisionAvoidanceFactor") ?? policy.CollisionAvoidanceFactor;
        policy.DelayPattern = options.String("delayPattern") ?? policy.DelayPattern;
        policy.AsyncDelayedRedelivery = options.Bool("asyncDelayedRedelivery") ?? policy.AsyncDelayedRedelivery;
        policy.LogRetryAttempted = options.Bool("logRetryAttempted") ?? policy.LogRetryAttempted;
        policy.RetryAttemptedLogLevel = options.LogLevel("retryAttemptedLogLevel") ?? policy.RetryAttemptedLogLevel;
        policy.RetriesExhaustedLogLevel = options.LogLevel("retriesExhaustedLogLevel") ?? policy.RetriesExhaustedLogLevel;
        policy.LogExhausted = options.Bool("logExhausted") ?? policy.LogExhausted;
        policy.LogHandled = options.Bool("logHandled") ?? policy.LogHandled;
        policy.LogContinued = options.Bool("logContinued") ?? policy.LogContinued;
        policy.LogStackTrace = options.Bool("logStackTrace") ?? policy.LogStackTrace;
        policy.LogRetryStackTrace = options.Bool("logRetryStackTrace") ?? policy.LogRetryStackTrace;
        policy.LogNewException = options.Bool("logNewException") ?? policy.LogNewException;
        policy.LogExhaustedMessageHistory = options.Bool("logExhaustedMessageHistory") ?? policy.LogExhaustedMessageHistory;
        policy.LogExhaustedExchange = options.Bool("logExhaustedMessageBody") ?? policy.LogExhaustedExchange;
    }

    /// <summary>The error handler definition of an <c>errorHandler</c> object.</summary>
    public static ErrorHandlerDefinition BuildErrorHandler(YamlObject options)
    {
        var given = options.Names.ToList();
        if (given.Count != 1)
        {
            throw options.Context.Error(
                options.Node,
                "errorHandler needs exactly one of deadLetterChannel, defaultErrorHandler, noErrorHandler, refErrorHandler" +
                (given.Count == 0 ? string.Empty : $" (found: {string.Join(", ", given)})"));
        }

        switch (given[0])
        {
            case "noErrorHandler":
                _ = options.Object("noErrorHandler");
                return new NoErrorHandlerDefinition();
            case "refErrorHandler":
                {
                    var reference = options.Object("refErrorHandler")!;
                    var name = reference.RequiredString("ref");
                    return options.Context.Context.Registry.Lookup(name) as ErrorHandlerDefinition
                        ?? throw reference.Error("ref", $"No error handler definition (ErrorHandlerDefinition) is bound in the registry as '{name}'");
                }

            case "deadLetterChannel":
                {
                    var dlc = options.Object("deadLetterChannel")!;
                    var definition = new DeadLetterChannelDefinition(dlc.RequiredString("deadLetterUri"))
                    {
                        HandleNewException = dlc.Bool("deadLetterHandleNewException") ?? true,
                    };
                    ApplyRedeliveryErrorHandler(dlc, definition);
                    return definition;
                }

            default:
                {
                    var handler = options.Object("defaultErrorHandler")!;
                    var definition = new DefaultErrorHandlerDefinition();
                    ApplyRedeliveryErrorHandler(handler, definition);
                    return definition;
                }
        }
    }

    /// <summary>Fills an <c>onException</c> definition (created with its exception types) from its options.</summary>
    public static void FillOnException(YamlObject options, OnExceptionDefinition definition)
    {
        definition.OnWhen = options.Predicate("onWhen");
        definition.Handled = options.Predicate("handled");
        definition.Continued = options.Predicate("continued");
        definition.RetryWhile = options.Predicate("retryWhile");
        ApplyRedeliveryPolicy(options.Object("redeliveryPolicy"), definition.RedeliveryPolicy);
        definition.UseOriginalMessage = options.Bool("useOriginalMessage") ?? false;
        definition.UseOriginalBody = options.Bool("useOriginalBody") ?? false;
        if (options.String("onRedeliveryRef") is { } onRedelivery)
        {
            definition.OnRedelivery = RegistryRefs.Processor(onRedelivery);
        }

        if (options.String("onExceptionOccurredRef") is { } onExceptionOccurred)
        {
            definition.OnExceptionOccurred = RegistryRefs.Processor(onExceptionOccurred);
        }

        options.Steps("steps", definition.Outputs);
        options.ApplyCommon(definition);
    }

    /// <summary>A new <c>onException</c> definition.</summary>
    public static OnExceptionDefinition BuildOnException(YamlObject options)
    {
        var types = options.Types("exception");
        if (types.Count == 0)
        {
            throw options.Error("exception", "onException needs at least one exception type");
        }

        foreach (var type in types.Where(t => !typeof(Exception).IsAssignableFrom(t)))
        {
            throw options.Error("exception", $"{type.FullName} is not an exception type");
        }

        var definition = new OnExceptionDefinition(types);
        FillOnException(options, definition);
        return definition;
    }

    /// <summary>Fills an <c>onCompletion</c> definition from its options.</summary>
    public static OnCompletionDefinition FillOnCompletion(YamlObject options, OnCompletionDefinition definition)
    {
        definition.Mode = options.Enum<OnCompletionMode>("mode") ?? OnCompletionMode.AfterConsumer;
        definition.OnCompleteOnly = options.Bool("onCompleteOnly") ?? false;
        definition.OnFailureOnly = options.Bool("onFailureOnly") ?? false;
        definition.OnWhen = options.Predicate("onWhen");
        definition.ParallelProcessing = options.Bool("parallelProcessing") ?? false;
        definition.UseOriginalMessage = options.Bool("useOriginalMessage") ?? false;
        if (definition.OnCompleteOnly && definition.OnFailureOnly)
        {
            throw options.Context.Error(options.Node, "onCompletion cannot be both onCompleteOnly and onFailureOnly");
        }

        options.Steps("steps", definition.Outputs);
        options.ApplyCommon(definition);
        return definition;
    }

    /// <summary>Fills an <c>intercept</c> or <c>interceptFrom</c> definition from its options.</summary>
    public static void FillIntercept(YamlObject options, InterceptDefinition definition)
    {
        definition.When = options.Predicate("onWhen");
        options.Steps("steps", definition.Outputs);
        options.ApplyCommon(definition);
    }

    /// <summary>Fills an <c>interceptSendToEndpoint</c> definition (created with its URI pattern) from its options.</summary>
    public static void FillInterceptSendToEndpoint(YamlObject options, InterceptSendToEndpointDefinition definition)
    {
        definition.SkipSendToOriginalEndpoint = options.Bool("skipSendToOriginalEndpoint") ?? false;
        definition.AfterUri = options.String("afterUri");
        definition.When = options.Predicate("onWhen");
        options.Steps("steps", definition.Outputs);
        options.ApplyCommon(definition);
    }

    private static void ApplyRedeliveryErrorHandler(YamlObject options, RedeliveryErrorHandlerDefinition definition)
    {
        ApplyRedeliveryPolicy(options.Object("redeliveryPolicy"), definition.RedeliveryPolicy);
        definition.UseOriginalMessage = options.Bool("useOriginalMessage") ?? false;
        definition.UseOriginalBody = options.Bool("useOriginalBody") ?? false;
        definition.LoggerName = options.String("logName");
        if (options.String("onRedeliveryRef") is { } onRedelivery)
        {
            definition.OnRedelivery = RegistryRefs.Processor(onRedelivery);
        }

        if (options.String("onExceptionOccurredRef") is { } onExceptionOccurred)
        {
            definition.OnExceptionOccurred = RegistryRefs.Processor(onExceptionOccurred);
        }

        if (options.String("onPrepareFailureRef") is { } onPrepareFailure)
        {
            definition.OnPrepareFailure = RegistryRefs.Processor(onPrepareFailure);
        }

        if (options.String("retryWhileRef") is { } retryWhile)
        {
            definition.RetryWhile = RegistryRefs.Predicate(retryWhile);
        }
    }
}
