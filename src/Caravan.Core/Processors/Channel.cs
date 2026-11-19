using Caravan.Model;
using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>
/// Wraps each node in a route. Records exceptions thrown by the node on the exchange and skips the
/// node when the exchange has been stopped. Feature-specific behaviour is added through intercept strategies.
/// </summary>
public sealed class Channel(RouteContext routeContext, ProcessorDefinition definition, IProcessor next) : INavigableProcessor
{
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
