using System.Text;
using Caravan.Support;

namespace Caravan.Language.Simple;

/// <summary>Text mixed with functions: the parts' values concatenated as a string (null values render as nothing).</summary>
internal sealed class TemplateNode(IReadOnlyList<SimpleNode> parts) : SimpleNode
{
    /// <inheritdoc />
    public override object? Evaluate(Exchange exchange)
    {
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            sb.Append(ObjectHelper.ToText(exchange, part.Evaluate(exchange)));
        }

        return sb.ToString();
    }
}
