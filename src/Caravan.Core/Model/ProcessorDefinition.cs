using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// A node in a route's definition tree (Camel's <c>ProcessorDefinition</c>). Definitions are plain data;
/// <see cref="CreateProcessor"/> turns them into runtime processors when the route starts.
/// </summary>
public abstract class ProcessorDefinition
{
    /// <summary>The node id; generated from <see cref="ShortName"/> when not set.</summary>
    public string? Id { get; set; }

    /// <summary>A human-readable description of the node.</summary>
    public string? Description { get; set; }

    /// <summary>Whether this node is disabled (skipped when the route is built).</summary>
    public bool Disabled { get; set; }

    /// <summary>The EIP name, e.g. <c>to</c>, <c>choice</c>, <c>split</c>. Used for node ids and YAML.</summary>
    public abstract string ShortName { get; }

    /// <summary>Child blocks for EIPs that contain nested steps (choice, split, doTry, ...).</summary>
    public virtual IEnumerable<ProcessorDefinition> Children => [];

    /// <summary>The parent node, or null for top-level nodes in a route.</summary>
    public ProcessorDefinition? Parent { get; internal set; }

    /// <summary>The route this node belongs to.</summary>
    public RouteDefinition? Route { get; internal set; }

    /// <summary>Builds the runtime processor for this node (without channel wrapping).</summary>
    public abstract IProcessor CreateProcessor(RouteContext routeContext);

    /// <summary>A short human-readable label, e.g. <c>to[mock:result]</c>.</summary>
    public virtual string Label => ShortName;

    /// <inheritdoc />
    public override string ToString() => Id is null ? Label : $"{Label} (id={Id})";
}
