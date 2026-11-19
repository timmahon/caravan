namespace Caravan.Model;

/// <summary>A route: an input endpoint followed by a list of steps.</summary>
public sealed class RouteDefinition
{
    public RouteDefinition(string fromUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromUri);
        FromUri = fromUri;
    }

    /// <summary>The route id; generated (<c>route1</c>, ...) when not set.</summary>
    public string? Id { get; set; }

    /// <summary>The URI of the endpoint the route consumes from.</summary>
    public string FromUri { get; set; }

    /// <summary>Optional id for the <c>from</c> node.</summary>
    public string? FromId { get; set; }

    public List<ProcessorDefinition> Outputs { get; } = [];

    public bool AutoStartup { get; set; } = true;

    /// <summary>Routes with a lower order start first and stop last.</summary>
    public int? StartupOrder { get; set; }

    public string? Description { get; set; }

    public string? Group { get; set; }

    /// <summary>Arbitrary route properties.</summary>
    public Dictionary<string, object?> Properties { get; } = new(StringComparer.Ordinal);

    /// <summary>Feature-specific configuration attached to the route (error handler, route policies, ...).</summary>
    public Dictionary<Type, object> Extensions { get; } = [];

    /// <summary>Enumerates all nodes in the route depth-first.</summary>
    public IEnumerable<ProcessorDefinition> AllNodes()
    {
        var stack = new Stack<ProcessorDefinition>(Outputs.AsEnumerable().Reverse());
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            foreach (var child in node.Children.Reverse())
            {
                stack.Push(child);
            }
        }
    }

    public override string ToString() => $"Route({Id ?? "?"})[{FromUri} -> {string.Join(", ", Outputs)}]";
}
