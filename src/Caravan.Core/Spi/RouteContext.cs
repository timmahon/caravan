using System.Collections.Concurrent;
using Caravan.Management;
using Caravan.Model;
using Caravan.Processors;

namespace Caravan.Spi;

/// <summary>
/// State used while turning a <see cref="RouteDefinition"/> into runtime processors.
/// EIP definitions use it to build child processors and to register services owned by the route.
/// </summary>
public sealed class RouteContext
{
    private readonly List<object> _services = [];
    private int _processorOrder;

    /// <summary>Creates the state for building the route <paramref name="routeId"/>.</summary>
    public RouteContext(ICaravanContext context, RouteDefinition definition, string routeId)
    {
        Context = context;
        Definition = definition;
        RouteId = routeId;
    }

    /// <summary>The context the route belongs to.</summary>
    public ICaravanContext Context { get; }

    /// <summary>The route being built.</summary>
    public RouteDefinition Definition { get; }

    /// <summary>The route id.</summary>
    public string RouteId { get; }

    /// <summary>The route configurations applied to the route, in order (see <see cref="RouteConfigurationDefinition"/>).</summary>
    public IReadOnlyList<RouteConfigurationDefinition> RouteConfigurations { get; private set; } = [];

    /// <summary>
    /// The route's error handler: its own or its route builder's (<see cref="RouteDefinition.ErrorHandler"/>), else the first applied route
    /// configuration's; null leaves the choice to the context.
    /// </summary>
    public ErrorHandlerDefinition? ErrorHandler =>
        Definition.ErrorHandler ?? RouteConfigurations.Select(c => c.ErrorHandler).FirstOrDefault(e => e is not null);

    /// <summary>
    /// The <c>onException</c> clauses of the route in precedence order: the route's own (route scoped), its builder's, then those of the
    /// applied route configurations.
    /// </summary>
    public IReadOnlyList<OnExceptionDefinition> OnExceptions =>
        Combine(Definition.OnExceptions, c => c.OnExceptions);

    /// <summary>
    /// The <c>onCompletion</c> blocks of the route: its own or its builder's (<see cref="RouteDefinition.OnCompletions"/>); when it has none,
    /// those of the applied route configurations.
    /// </summary>
    public IReadOnlyList<OnCompletionDefinition> OnCompletions =>
        Definition.OnCompletions.Count > 0 ? Definition.OnCompletions : Combine([], c => c.OnCompletions);

    /// <summary>The <c>intercept</c> blocks of the route: its own (builder, advice), then those of the applied route configurations.</summary>
    public IReadOnlyList<InterceptDefinition> Intercepts => Combine(Definition.Intercepts, c => c.Intercepts);

    /// <summary>The <c>interceptFrom</c> blocks of the route: its own, then those of the applied route configurations.</summary>
    public IReadOnlyList<InterceptFromDefinition> InterceptFroms => Combine(Definition.InterceptFroms, c => c.InterceptFroms);

    /// <summary>The <c>interceptSendToEndpoint</c> definitions of the route: its own, then those of the applied route configurations.</summary>
    public IReadOnlyList<InterceptSendToEndpointDefinition> InterceptSendToEndpoints =>
        Combine(Definition.InterceptSendToEndpoints, c => c.InterceptSendToEndpoints);

    /// <summary>Services (processors, producers, ...) started and stopped with the route.</summary>
    public IReadOnlyList<object> Services => _services;

    /// <summary>
    /// Wrappers applied around every node's processor in addition to the context's intercept strategies.
    /// Optional features (error handling, advice) add to this list.
    /// </summary>
    public IList<IInterceptStrategy> InterceptStrategies { get; } = [];

    /// <summary>
    /// The context's stream caching strategy when the route caches streams (<see cref="RouteDefinition.StreamCaching"/>, else the
    /// strategy's <see cref="Streams.StreamCachingStrategy.Enabled"/>), otherwise null.
    /// </summary>
    public Streams.StreamCachingStrategy? StreamCachingStrategy =>
        Definition.StreamCaching ?? Context.StreamCaching.Enabled ? Context.StreamCaching : null;

    /// <summary>Wraps each node with error handling (set by the error handling feature).</summary>
    public IErrorHandlerFactory? ErrorHandlerFactory { get; set; }

    /// <summary>The route's performance counters (management, <c>controlbus</c> stats).</summary>
    internal ProcessingCounters Counters { get; } = new();

    /// <summary>The performance counters of the route's nodes, by definition (when node statistics are enabled).</summary>
    internal ConcurrentDictionary<ProcessorDefinition, ProcessorCounters> ProcessorCounters { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>The counters of a node, created on first use.</summary>
    internal ProcessorCounters GetProcessorCounters(ProcessorDefinition definition) =>
        ProcessorCounters.GetOrAdd(definition, d => new ProcessorCounters(d, d.Id ?? d.ShortName, Interlocked.Increment(ref _processorOrder)));

    /// <summary>Registers a service started and stopped with the route (added once).</summary>
    public void AddService(object service)
    {
        if (!_services.Contains(service))
        {
            _services.Add(service);
        }
    }

    /// <summary>Creates the processor for one node, wrapped in a channel. Returns null for disabled nodes.</summary>
    public IProcessor? CreateNodeProcessor(ProcessorDefinition definition)
    {
        if (definition.Disabled)
        {
            return null;
        }

        definition.Id ??= Context.NextNodeId(definition.ShortName);
        definition.Route ??= Definition;

        var processor = definition.CreateProcessor(this);
        AddService(processor);

        if (ErrorHandlerFactory is not null)
        {
            processor = ErrorHandlerFactory.Wrap(this, definition, processor);
            AddService(processor);
        }

        foreach (var strategy in InterceptStrategies.Concat(Context.InterceptStrategies).Reverse())
        {
            processor = strategy.WrapProcessor(this, definition, processor);
        }

        var channel = new Channel(this, definition, processor);
        return channel;
    }

    /// <summary>Creates a pipeline for a block of steps.</summary>
    public IProcessor CreateBlockProcessor(IEnumerable<ProcessorDefinition> outputs, ProcessorDefinition? parent = null)
    {
        var processors = new List<IProcessor>();
        var list = outputs as IReadOnlyList<ProcessorDefinition> ?? [.. outputs];
        for (var i = 0; i < list.Count; i++)
        {
            var output = list[i];
            if (parent is not null)
            {
                output.Parent = parent;
            }

            if (output is ThreadsDefinition { Outputs.Count: 0, Disabled: false } threads)
            {
                // Chained threads: the rest of the block runs on its workers.
                foreach (var rest in list.Skip(i + 1))
                {
                    if (parent is not null)
                    {
                        rest.Parent = parent;
                    }
                }

                threads.RestOfBlock = [.. list.Skip(i + 1)];
                processors.Add(CreateNodeProcessor(threads)!);
                break;
            }

            var processor = CreateNodeProcessor(output);
            if (processor is not null)
            {
                processors.Add(processor);
            }
        }

        return processors.Count == 1 ? processors[0] : new Pipeline(processors);
    }

    /// <summary>
    /// Selects the route configurations of the route among <paramref name="configurations"/> (Camel's rules): with
    /// <see cref="RouteDefinition.RouteConfigurationIds"/>, for each id or pattern in order, the configurations with a matching id; without, the
    /// configurations without id (or with id <c>*</c>).
    /// </summary>
    /// <exception cref="ArgumentException">An id without wildcard matches no configuration.</exception>
    internal void ApplyRouteConfigurations(IReadOnlyList<RouteConfigurationDefinition> configurations)
    {
        var selected = new List<RouteConfigurationDefinition>();
        if (Definition.RouteConfigurationIds is not { } ids)
        {
            selected.AddRange(configurations.Where(c => c.Id is null or "*"));
        }
        else
        {
            foreach (var id in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var matches = configurations.Where(c => c.Id is not null && Support.PatternHelper.Matches(c.Id, id)).ToList();
                if (matches.Count == 0 && !id.Contains('*', StringComparison.Ordinal) && !id.Any(c => c is '^' or '$' or '[' or '(' or '|' or '?' or '+'))
                {
                    throw new ArgumentException($"Route configuration with id {id} not found");
                }

                selected.AddRange(matches.Where(m => !selected.Contains(m)));
            }
        }

        RouteConfigurations = selected;
    }

    private IReadOnlyList<T> Combine<T>(List<T> own, Func<RouteConfigurationDefinition, List<T>> select)
        where T : class =>
        RouteConfigurations.Count == 0 ? own : [.. own, .. RouteConfigurations.SelectMany(select).Where(d => !own.Contains(d))];

    /// <summary>Resolves an endpoint, applying property placeholders.</summary>
    public IEndpoint ResolveEndpoint(string uri) => Context.GetEndpoint(uri);
}
