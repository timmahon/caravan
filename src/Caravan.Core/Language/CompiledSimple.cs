using Caravan.Language.Simple;

namespace Caravan.Language;

/// <summary>A compiled Simple text: its tree, or the parse error deferred until its placeholders are resolved.</summary>
/// <param name="Node">The tree; null when the text only parses after resolving placeholders.</param>
/// <param name="Error">The parse error of the unresolved text, when <paramref name="Node"/> is null.</param>
/// <param name="HasPlaceholders">Whether the text contains <c>{{</c>, resolved per evaluation.</param>
internal sealed record CompiledSimple(SimpleNode? Node, SimpleParseException? Error, bool HasPlaceholders)
{
    /// <summary>The tree to evaluate for <paramref name="exchange"/>, after resolving placeholders.</summary>
    public SimpleNode Resolve(string text, Exchange exchange, Func<string, CompiledSimple> compile)
    {
        if (HasPlaceholders)
        {
            var resolved = exchange.Context.PropertiesComponent.ResolvePlaceholders(text);
            if (resolved != text)
            {
                var compiled = compile(resolved);
                return compiled.Node ?? throw compiled.Error!;
            }
        }

        return Node ?? throw Error!;
    }
}
