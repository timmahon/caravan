using System.Text;
using Caravan.Model;
using Caravan.Observability;

namespace Caravan.Management;

/// <summary>Writes a route definition as indented text.</summary>
internal static class RouteModelDumper
{
    public static string Dump(RouteDefinition route)
    {
        var builder = new StringBuilder();
        builder.Append("route (id=").Append(route.Id).Append(')');
        if (route.Group is { } group)
        {
            builder.Append(" group=").Append(group);
        }

        if (route.Description is { } description)
        {
            builder.Append(" description=").Append(description);
        }

        builder.AppendLine();
        builder.Append("  from[").Append(UriSanitizer.Sanitize(route.FromUri)).AppendLine("]");
        foreach (var node in route.Outputs)
        {
            Write(builder, node, 1);
        }

        return builder.ToString();
    }

    private static void Write(StringBuilder builder, ProcessorDefinition node, int depth)
    {
        builder.Append(' ', 2 * depth).Append(node.Label);
        if (node.Id is { } id)
        {
            builder.Append(" (id=").Append(id).Append(')');
        }

        if (node.Disabled)
        {
            builder.Append(" [disabled]");
        }

        builder.AppendLine();
        foreach (var child in node.Children)
        {
            Write(builder, child, depth + 1);
        }
    }
}
