using Caravan.Model;

namespace Caravan.Spi;

/// <summary>
/// Wraps the processor of each node in a route. Used for tracing, metrics, message history,
/// interception and testing advice (Camel's <c>InterceptStrategy</c>).
/// </summary>
public interface IInterceptStrategy
{
    /// <summary>Returns a processor wrapping <paramref name="target"/>, or <paramref name="target"/> itself.</summary>
    IProcessor WrapProcessor(RouteContext routeContext, ProcessorDefinition definition, IProcessor target);
}
