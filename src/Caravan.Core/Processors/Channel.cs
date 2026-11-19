using Caravan.Model;
using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>
/// Wraps each node in a route. Records exceptions thrown by the node on the exchange and skips the
/// node when the exchange has been stopped. When the route caches streams, a stream body is turned into a stream cache, or a stream
/// cache body reset, before the node runs. Feature-specific behaviour is added through intercept strategies.
/// </summary>
public sealed class Channel(RouteContext routeContext, ProcessorDefinition definition, IProcessor next) : INavigableProcessor
{
    private readonly Streams.StreamCachingStrategy? _streamCaching = routeContext.StreamCachingStrategy;

    /// <summary>The route the node belongs to.</summary>
    public RouteContext RouteContext { get; } = routeContext;

    /// <summary>The definition of the wrapped node.</summary>
    public ProcessorDefinition Definition { get; } = definition;

    /// <summary>The node processor, wrapped by error handling and intercept strategies.</summary>
    public IProcessor Target { get; } = next;

    /// <inheritdoc />
    public IReadOnlyList<IProcessor> Next => [Target];

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        if (exchange.IsRouteStop)
        {
            return;
        }

        try
        {
            if (_streamCaching is not null && exchange.Message.Body is Stream)
            {
                await _streamCaching.CacheAsync(exchange).ConfigureAwait(false);
            }

            await Target.ProcessAsync(exchange).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            exchange.Exception = e;
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"Channel[{Definition}]";
}
