using Caravan.Spi;

namespace Caravan;

/// <summary>Callbacks invoked for every route before its processors are built (used by optional features).</summary>
public sealed class RouteContextConfigurers
{
    /// <summary>Callbacks run for each route before its processors are built.</summary>
    public List<Action<RouteContext>> Configurers { get; } = [];
}
