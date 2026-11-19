using Caravan.Model;

namespace Caravan.Spi;

/// <summary>Wraps a node's processor with error handling (redelivery, dead letter channel, ...).</summary>
public interface IErrorHandlerFactory
{
    /// <summary>Wraps the processor of the node <paramref name="definition"/> with error handling.</summary>
    IProcessor Wrap(RouteContext routeContext, ProcessorDefinition definition, IProcessor target);
}
