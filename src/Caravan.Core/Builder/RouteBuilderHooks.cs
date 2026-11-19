using Caravan.Model;

namespace Caravan.Builder;

/// <summary>
/// Hooks invoked after a <see cref="RouteBuilder"/> has configured its routes. Features such as
/// route-scoped configuration use this to post-process definitions.
/// </summary>
public sealed class RouteBuilderHooks
{
    /// <summary>Callbacks receiving each builder and the definitions it produced, in registration order.</summary>
    public List<Action<RouteBuilder, List<RouteDefinition>>> Hooks { get; } = [];
}
