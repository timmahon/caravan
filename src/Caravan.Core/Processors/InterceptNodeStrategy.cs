using Caravan.Model;
using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>
/// The <see cref="IInterceptStrategy"/> of one <c>intercept</c> definition in one route: wraps every node with an
/// <see cref="InterceptProcessor"/>, except the nodes of intercept, <c>onException</c> and <c>onCompletion</c> blocks.
/// </summary>
internal sealed class InterceptNodeStrategy(InterceptDefinition definition, IProcessor block, string? routeEndpointUri) : IInterceptStrategy
{
    public IProcessor WrapProcessor(RouteContext routeContext, ProcessorDefinition node, IProcessor target) =>
        IsExcluded(node)
            ? target
            : new InterceptProcessor(definition.When, block, target, routeContext.RouteId, node.Id, routeEndpointUri);

    /// <summary>Whether <paramref name="node"/> belongs to a block that intercepts do not apply to.</summary>
    internal static bool IsExcluded(ProcessorDefinition node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (current is InterceptDefinition or InterceptSendToEndpointDefinition or OnExceptionDefinition or OnCompletionDefinition)
            {
                return true;
            }
        }

        return false;
    }
}
