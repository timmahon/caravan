using Caravan.Support;

namespace Caravan.Language.Simple;

/// <summary>
/// A function whose text contains nested functions, e.g. <c>${header.${header.name}}</c>: the nested functions are evaluated first,
/// then the resulting function text is compiled (cached) and evaluated.
/// </summary>
internal sealed class DynamicFunctionNode(SimpleNode text) : SimpleNode
{
    /// <inheritdoc />
    public override object? Evaluate(Exchange exchange)
    {
        var function = ObjectHelper.ToText(exchange, text.Evaluate(exchange)) ?? string.Empty;
        return SimpleLanguage.CompileFunction(function).Evaluate(exchange);
    }
}
