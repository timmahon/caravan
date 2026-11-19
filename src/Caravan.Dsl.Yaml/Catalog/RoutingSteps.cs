using Caravan.Model;

namespace Caravan.Dsl.Yaml.Catalog;

/// <summary>Steps routing the exchange: blocks, conditions, fan-out, dynamic endpoints, enrichment.</summary>
internal static class RoutingSteps
{
    private static readonly OptionSpec AggregationStrategy =
        Opt.Ref("aggregationStrategy", "The aggregation strategy").Alias("strategyRef", "aggregationStrategyRef");

    private static readonly OptionSpec[] FanOutOptions =
    [
        AggregationStrategy,
        Opt.Bool("parallelProcessing", "Processes the branches concurrently."),
        Opt.Bool("parallelAggregate", "Calls the aggregation strategy concurrently (with streaming)."),
        Opt.Int("maxDegreeOfParallelism", "Caravan: the maximum number of branches running at once with parallelProcessing (default 10)."),
        Opt.Bool("streaming", "Aggregates in completion order (and splits lazily)."),
        Opt.Bool("stopOnException", "Stops at the first failed branch."),
        Opt.Duration("timeout", "With parallelProcessing: gives up on branches not finished after this time."),
        Opt.Ref("onPrepare", "A processor preparing each branch's exchange").Alias("onPrepareRef"),
        Opt.Bool("shareUnitOfWork", "Branches share the unit of work of the input exchange."),
        Opt.Ignored("executorService", OptionKind.String, "branches run on the thread pool"),
        Opt.Ignored("synchronous", OptionKind.Boolean, "processing is asynchronous"),
        Opt.Unsupported("aggregationStrategyMethodName", "use a strategy implementing IAggregationStrategy"),
        Opt.Unsupported("aggregationStrategyMethodAllowNull", "use a strategy implementing IAggregationStrategy"),
    ];

    private static readonly OptionSpec[] DynamicSendOptions =
    [
        Opt.Bool("ignoreInvalidEndpoints", "Ignores URIs that do not resolve to an endpoint."),
        Opt.Int("cacheSize", "Size of the cache of producers."),
    ];

    private static readonly ObjectSpec Clause = new() { CommonOptions = true, Options = [Opt.StepsOption] };

    /// <summary>The steps.</summary>
    public static IEnumerable<StepSpec> All =>
    [
        new()
        {
            Name = "pipeline",
            Description = "Runs its steps in order (a block).",
            Options = [Opt.StepsOption],
            Build = o => Block(o, new PipelineDefinition()),
        },
        new()
        {
            Name = "step",
            Description = "Groups steps under an id (a block with its own statistics).",
            Options = [Opt.StepsOption],
            Build = o => Block(o, new StepDefinition()),
        },
        new()
        {
            Name = "stop",
            Description = "Stops routing the exchange.",
            Build = _ => new StopDefinition(),
        },
        new()
        {
            Name = "filter",
            Description = "Runs its steps only for exchanges matching the predicate.",
            Expression = ExpressionSlot.Predicate,
            Options = [Opt.StepsOption, Opt.Unsupported("statusPropertyName", "the result is in the CaravanFilterMatched property")],
            Build = o => Block(o, new FilterDefinition(o.InlinePredicate())),
        },
        new()
        {
            Name = "choice",
            Description = "Content-based router: the steps of the first matching when, else otherwise.",
            Options =
            [
                Opt.Objects(
                    "when",
                    "The conditions, in order.",
                    new ObjectSpec { CommonOptions = true, Expression = ExpressionSlot.Predicate, Options = [Opt.StepsOption] },
                    required: true),
                Opt.Object("otherwise", "The steps when no condition matches.", Clause),
                Opt.Unsupported("precondition", "choice preconditions are not supported"),
            ],
            Build = o =>
            {
                var choice = new ChoiceDefinition();
                foreach (var when in o.Objects("when"))
                {
                    var clause = new WhenDefinition(when.InlinePredicate());
                    when.Steps("steps", clause.Outputs);
                    when.ApplyCommon(clause);
                    choice.WhenClauses.Add(clause);
                }

                if (o.Object("otherwise") is { } otherwise)
                {
                    choice.Otherwise = new OtherwiseDefinition();
                    otherwise.Steps("steps", choice.Otherwise.Outputs);
                    otherwise.ApplyCommon(choice.Otherwise);
                }

                return choice;
            },
        },
        new()
        {
            Name = "split",
            Description = "Splits the message into parts (the expression's items) processed by the steps.",
            Expression = ExpressionSlot.Expression,
            Options = [Opt.Str("delimiter", "Splits a text value on this delimiter."), .. FanOutOptions, Opt.StepsOption],
            Build = o =>
            {
                var split = new SplitDefinition(o.InlineExpression()) { Delimiter = o.String("delimiter") };
                ApplyFanOut(o, split);
                o.Steps("steps", split.Outputs);
                return split;
            },
        },
        new()
        {
            Name = "multicast",
            Description = "Sends a copy of the exchange to each step (each step is a branch).",
            Options = [.. FanOutOptions, Opt.StepsOption],
            Build = o =>
            {
                var multicast = new MulticastDefinition();
                ApplyFanOut(o, multicast);
                o.Steps("steps", multicast.Outputs);
                return multicast;
            },
        },
        new()
        {
            Name = "recipientList",
            Description = "Sends a copy of the exchange to each endpoint of the expression's value.",
            Expression = ExpressionSlot.Expression,
            Options = [Opt.Str("delimiter", "Splits a text value on this delimiter (default ,)."), .. FanOutOptions, .. DynamicSendOptions],
            Build = o =>
            {
                var list = new RecipientListDefinition(o.InlineExpression());
                list.Delimiter = o.String("delimiter") ?? list.Delimiter;
                list.IgnoreInvalidEndpoints = o.Bool("ignoreInvalidEndpoints") ?? false;
                list.CacheSize = o.Int("cacheSize") ?? list.CacheSize;
                ApplyFanOut(o, list);
                return list;
            },
        },
        new()
        {
            Name = "routingSlip",
            Description = "Sends the exchange through the endpoints of the expression's value, in order.",
            Expression = ExpressionSlot.Expression,
            Options = [Opt.Str("uriDelimiter", "Splits a text value on this delimiter (default ,)."), .. DynamicSendOptions],
            Build = o =>
            {
                var slip = new RoutingSlipDefinition(o.InlineExpression());
                slip.UriDelimiter = o.String("uriDelimiter") ?? slip.UriDelimiter;
                slip.IgnoreInvalidEndpoints = o.Bool("ignoreInvalidEndpoints") ?? false;
                slip.CacheSize = o.Int("cacheSize") ?? slip.CacheSize;
                return slip;
            },
        },
        new()
        {
            Name = "dynamicRouter",
            Description = "Sends the exchange to the endpoints the expression returns, evaluated again after each step until it returns null.",
            Expression = ExpressionSlot.Expression,
            Options = [Opt.Str("uriDelimiter", "Splits a text value on this delimiter (default ,)."), .. DynamicSendOptions],
            Build = o =>
            {
                var router = new DynamicRouterDefinition(o.InlineExpression());
                router.UriDelimiter = o.String("uriDelimiter") ?? router.UriDelimiter;
                router.IgnoreInvalidEndpoints = o.Bool("ignoreInvalidEndpoints") ?? false;
                router.CacheSize = o.Int("cacheSize") ?? router.CacheSize;
                return router;
            },
        },
        new()
        {
            Name = "wireTap",
            Description = "Sends a copy of the exchange to an endpoint (a Simple expression) in the background.",
            InlineOption = "uri",
            Options =
            [
                Opt.Str("uri", "The endpoint URI (Simple).", required: true),
                Opt.Map("parameters", "Endpoint options appended to the URI as query parameters."),
                Opt.Bool("copy", "Sends a copy of the exchange (default true)."),
                Opt.Ref("onPrepare", "A processor preparing the copy").Alias("onPrepareRef"),
                Opt.Int("cacheSize", "Size of the cache of producers."),
                Opt.Bool("ignoreInvalidEndpoint", "Ignores URIs that do not resolve to an endpoint."),
                Opt.Ignored("dynamicUri", OptionKind.Boolean, "the URI is always a Simple expression"),
                Opt.Ignored("executorService", OptionKind.String, "the tap runs on the thread pool"),
                Opt.Ignored("allowOptimisedComponents", OptionKind.Boolean, "no optimised components"),
                Opt.Ignored("autoStartComponents", OptionKind.Boolean, "components start on use"),
                Opt.Unsupported("pattern", "a wire tap always sends InOnly"),
            ],
            Build = o =>
            {
                var tap = new WireTapDefinition(BasicSteps.Simple(o, EndpointUris.Build(o)));
                tap.Copy = o.Bool("copy") ?? tap.Copy;
                tap.CacheSize = o.Int("cacheSize") ?? tap.CacheSize;
                tap.IgnoreInvalidEndpoint = o.Bool("ignoreInvalidEndpoint") ?? false;
                tap.OnPrepare = o.String("onPrepare") is { } prepare ? RegistryRefs.OnPrepare(prepare) : null;
                return tap;
            },
        },
        new()
        {
            Name = "enrich",
            Description = "Merges the reply of a resource endpoint (the expression, or uri) into the exchange.",
            Expression = ExpressionSlot.Expression,
            ExpressionRequired = false,
            Options =
            [
                Opt.Str("uri", "Caravan: the resource URI (Simple), instead of an expression."),
                AggregationStrategy,
                Opt.Bool("aggregateOnException", "Calls the strategy when the resource failed."),
                Opt.Bool("shareUnitOfWork", "The resource exchange shares the unit of work."),
                Opt.Bool("ignoreInvalidEndpoint", "Ignores URIs that do not resolve to an endpoint."),
                Opt.Int("cacheSize", "Size of the cache of producers."),
                Opt.Ignored("allowOptimisedComponents", OptionKind.Boolean, "no optimised components"),
                Opt.Ignored("autoStartComponents", OptionKind.Boolean, "components start on use"),
                Opt.Unsupported("aggregationStrategyMethodName", "use a strategy implementing IAggregationStrategy"),
                Opt.Unsupported("aggregationStrategyMethodAllowNull", "use a strategy implementing IAggregationStrategy"),
                Opt.Unsupported("variableSend", "variables are not supported"),
                Opt.Unsupported("variableReceive", "variables are not supported"),
            ],
            Build = o =>
            {
                var enrich = new EnrichDefinition(ResourceUri(o))
                {
                    AggregationStrategyRef = o.String("aggregationStrategy"),
                    AggregateOnException = o.Bool("aggregateOnException") ?? false,
                    ShareUnitOfWork = o.Bool("shareUnitOfWork") ?? false,
                    IgnoreInvalidEndpoint = o.Bool("ignoreInvalidEndpoint") ?? false,
                };
                enrich.CacheSize = o.Int("cacheSize") ?? enrich.CacheSize;
                return enrich;
            },
        },
        new()
        {
            Name = "pollEnrich",
            Description = "Merges a message polled from a resource endpoint (the expression, or uri) into the exchange.",
            Expression = ExpressionSlot.Expression,
            ExpressionRequired = false,
            Options =
            [
                Opt.Str("uri", "Caravan: the resource URI (Simple), instead of an expression."),
                Opt.Duration("timeout", "How long to wait for a message (negative: forever, the default; 0: do not wait)."),
                AggregationStrategy,
                Opt.Bool("aggregateOnException", "Calls the strategy when the poll failed."),
                Opt.Bool("ignoreInvalidEndpoint", "Ignores URIs that do not resolve to an endpoint."),
                Opt.Int("cacheSize", "Size of the cache of consumers."),
                Opt.Ignored("autoStartComponents", OptionKind.Boolean, "components start on use"),
                Opt.Unsupported("aggregationStrategyMethodName", "use a strategy implementing IAggregationStrategy"),
                Opt.Unsupported("aggregationStrategyMethodAllowNull", "use a strategy implementing IAggregationStrategy"),
                Opt.Unsupported("variableReceive", "variables are not supported"),
            ],
            Build = o =>
            {
                var timeout = o.Duration("timeout");
                var enrich = new PollEnrichDefinition(ResourceUri(o))
                {
                    Timeout = timeout is { } t && t < TimeSpan.Zero ? null : timeout,
                    AggregationStrategyRef = o.String("aggregationStrategy"),
                    AggregateOnException = o.Bool("aggregateOnException") ?? false,
                    IgnoreInvalidEndpoint = o.Bool("ignoreInvalidEndpoint") ?? false,
                };
                enrich.CacheSize = o.Int("cacheSize") ?? enrich.CacheSize;
                return enrich;
            },
        },
        new()
        {
            Name = "claimCheck",
            Description = "Stores or retrieves (parts of) the message in a per-exchange claim check repository.",
            Options =
            [
                Opt.Enum("operation", "The operation.", "Get", "GetAndRemove", "Set", "Push", "Pop").Req(),
                Opt.Str("key", "The key (Simple)."),
                Opt.Str("filter", "What to merge back: body, headers, header:pattern, with + and - prefixes."),
                AggregationStrategy,
                Opt.Unsupported("aggregationStrategyMethodName", "use a strategy implementing IAggregationStrategy"),
            ],
            Build = o => new ClaimCheckDefinition(o.Enum<ClaimCheckOperation>("operation")!.Value)
            {
                Key = o.String("key"),
                Filter = o.String("filter"),
                AggregationStrategyRef = o.String("aggregationStrategy"),
            },
        },
    ];

    /// <summary>Reads the steps of a block definition.</summary>
    public static ProcessorDefinition Block(YamlObject options, OutputDefinition definition)
    {
        options.Steps("steps", definition.Outputs);
        return definition;
    }

    private static void ApplyFanOut(YamlObject o, FanOutDefinition definition)
    {
        definition.AggregationStrategyRef = o.String("aggregationStrategy");
        definition.ParallelProcessing = o.Bool("parallelProcessing") ?? false;
        definition.ParallelAggregate = o.Bool("parallelAggregate") ?? false;
        definition.MaxDegreeOfParallelism = o.Int("maxDegreeOfParallelism") ?? 0;
        definition.Streaming = o.Bool("streaming") ?? false;
        definition.StopOnException = o.Bool("stopOnException") ?? false;
        definition.Timeout = o.Duration("timeout") is { } timeout && timeout > TimeSpan.Zero ? timeout : null;
        definition.ShareUnitOfWork = o.Bool("shareUnitOfWork") ?? false;
        definition.OnPrepare = o.String("onPrepare") is { } prepare ? RegistryRefs.OnPrepare(prepare) : null;
    }

    private static IExpression ResourceUri(YamlObject o)
    {
        var uri = o.String("uri");
        var expression = o.OptionalInlineExpression();
        return (uri, expression) switch
        {
            ({ } text, null) => BasicSteps.Simple(o, text),
            (null, { } value) => value,
            _ => throw o.Context.Error(o.Node, $"'{o.Element}' needs either an expression (e.g. constant: \"direct:resource\") or uri"),
        };
    }
}
