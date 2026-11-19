using Caravan.Model;

namespace Caravan.Spi;

/// <summary>A runtime route.</summary>
public interface IRoute
{
    /// <summary>The route id.</summary>
    string Id { get; }

    /// <summary>The route's definition.</summary>
    RouteDefinition Definition { get; }

    /// <summary>The endpoint the route consumes from.</summary>
    IEndpoint Endpoint { get; }

    /// <summary>The consumer, while the route is started.</summary>
    IConsumer? Consumer { get; }

    /// <summary>The entry processor of the route.</summary>
    IProcessor Processor { get; }

    /// <summary>The lifecycle status of the route.</summary>
    ServiceStatus Status { get; }

    /// <summary>Number of exchanges currently being routed.</summary>
    int InflightCount { get; }

    /// <summary>Arbitrary runtime properties of the route.</summary>
    IDictionary<string, object?> Properties { get; }
}
