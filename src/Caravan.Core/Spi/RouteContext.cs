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
        foreach (var output in outputs)
        {
            if (parent is not null)
            {
                output.Parent = parent;
            }

            var processor = CreateNodeProcessor(output);
            if (processor is not null)
            {
                processors.Add(processor);
            }
        }

        return processors.Count == 1 ? processors[0] : new Pipeline(processors);
    }

    /// <summary>Resolves an endpoint, applying property placeholders.</summary>
    public IEndpoint ResolveEndpoint(string uri) => Context.GetEndpoint(uri);
}
