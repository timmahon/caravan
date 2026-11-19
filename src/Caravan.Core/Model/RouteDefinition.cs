namespace Caravan.Model;

/// <summary>A route: an input endpoint followed by a list of steps.</summary>
public sealed class RouteDefinition
{
    /// <summary>Creates a route consuming from <paramref name="fromUri"/>.</summary>
    public RouteDefinition(string fromUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromUri);
        FromUri = fromUri;
    }

    /// <summary>The route id; generated (<c>route1</c>, ...) when not set.</summary>
    public string? Id { get; set; }

    /// <summary>The URI of the endpoint the route consumes from.</summary>
    public string FromUri { get; set; }

    /// <summary>Optional id for the <c>from</c> node.</summary>
    public string? FromId { get; set; }

    /// <summary>The route's top-level steps.</summary>
    public List<ProcessorDefinition> Outputs { get; } = [];

    /// <summary>Whether the route starts with the context (default true).</summary>
    public bool AutoStartup { get; set; } = true;

    /// <summary>Routes with a lower order start first and stop last.</summary>
    public int? StartupOrder { get; set; }

    /// <summary>When the route's consumer stops during a graceful shutdown (default <see cref="Caravan.ShutdownRoute.Default"/>).</summary>
    public ShutdownRoute ShutdownRoute { get; set; } = ShutdownRoute.Default;

    /// <summary>How much pending work the route's consumer completes during a graceful shutdown (default <see cref="Caravan.ShutdownRunningTask.CompleteCurrentTaskOnly"/>).</summary>
    public ShutdownRunningTask ShutdownRunningTask { get; set; } = ShutdownRunningTask.CompleteCurrentTaskOnly;

    /// <summary>A human-readable description of the route.</summary>
    public string? Description { get; set; }

    /// <summary>The name of the group the route belongs to.</summary>
    public string? Group { get; set; }

    /// <summary>
    /// Whether the route caches stream bodies (see <see cref="Streams.StreamCachingStrategy"/>); null (default) follows the context's
    /// <see cref="Streams.StreamCachingStrategy.Enabled"/>.
    /// </summary>
    public bool? StreamCaching { get; set; }

    /// <summary>Arbitrary route properties.</summary>
    public Dictionary<string, object?> Properties { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The route's error handler; when null the route builder's, then the context's (<see cref="ICaravanContext.ErrorHandler"/>),
    /// then a <see cref="DefaultErrorHandlerDefinition"/> is used.
    /// </summary>
    public ErrorHandlerDefinition? ErrorHandler { get; set; }

    /// <summary>The <c>onException</c> clauses applying to the route: its own (route scoped) and its route builder's.</summary>
    public List<OnExceptionDefinition> OnExceptions { get; } = [];

    /// <summary>The <c>onCompletion</c> blocks of the route: its own, or its route builder's when it has none.</summary>
    public List<OnCompletionDefinition> OnCompletions { get; } = [];

    /// <summary>
    /// The <c>intercept</c> blocks applying to the route (run before each of its nodes): its route builder's, and those added by
    /// route configurations or advice.
    /// </summary>
    public List<InterceptDefinition> Intercepts { get; } = [];

    /// <summary>The <c>interceptFrom</c> blocks applying to the route (run when an exchange enters it, if its endpoint matches).</summary>
    public List<InterceptFromDefinition> InterceptFroms { get; } = [];

    /// <summary>
    /// The <c>interceptSendToEndpoint</c> definitions brought by the route: they are registered with the context (and then apply to every
    /// send in it) when the route is created.
    /// </summary>
    public List<InterceptSendToEndpointDefinition> InterceptSendToEndpoints { get; } = [];

    /// <summary>
    /// The ids of the route configurations the route uses (Camel's <c>routeConfigurationId</c>): a comma-separated list of ids or patterns
    /// (wildcard <c>cfg*</c> or regular expression), applied in that order. Null (default) applies the configurations without id.
    /// </summary>
    public string? RouteConfigurationIds { get; set; }

    /// <summary>The id of the template the route was created from, or null.</summary>
    public string? TemplateId { get; set; }

    /// <summary>The parameters the route was created from its template with (null for routes not created from a template).</summary>
    public IReadOnlyDictionary<string, object?>? TemplateParameters { get; set; }

    /// <summary>
    /// The route's local beans (template beans), looked up before the context's registry while the route is created and while it processes
    /// exchanges; null when the route has none.
    /// </summary>
    public Impl.LocalBeanRegistry? LocalBeans { get; set; }

    /// <summary>Whether the route was created by the <c>kamelet</c> component (such routes are never filtered out).</summary>
    internal bool IsKamelet { get; set; }

    /// <summary>Feature-specific configuration attached to the route (error handler, route policies, ...).</summary>
    public Dictionary<Type, object> Extensions { get; } = [];

    /// <summary>Enumerates all nodes in the route depth-first.</summary>
    public IEnumerable<ProcessorDefinition> AllNodes()
    {
        var stack = new Stack<ProcessorDefinition>(Outputs.AsEnumerable().Reverse());
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            foreach (var child in node.Children.Reverse())
            {
                stack.Push(child);
            }
        }
    }

    /// <summary>
    /// Creates a deep copy of the route: its nodes (nested blocks included), <c>onException</c>/<c>onCompletion</c>/intercept definitions,
    /// error handler and option objects are copied, so changing the copy (e.g. with AdviceWith) leaves this route unchanged. Expressions,
    /// predicates, delegates, bean instances and other runtime objects are shared. <see cref="LocalBeans"/> is shared too.
    /// </summary>
    /// <returns>The copy.</returns>
    public RouteDefinition Clone() => Support.DefinitionCloner.Clone(this, null);

    /// <inheritdoc />
    public override string ToString() => $"Route({Id ?? "?"})[{FromUri} -> {string.Join(", ", Outputs)}]";
}
