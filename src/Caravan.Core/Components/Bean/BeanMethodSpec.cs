namespace Caravan.Components.Bean;

/// <summary>
/// A parsed method specification: <c>name</c>, <c>name()</c>, <c>name(string, int)</c> (parameter types choose the overload) or
/// <c>name(${body}, 'text', 5, true, null)</c> (values, one per parameter).
/// </summary>
internal sealed class BeanMethodSpec
{
    private BeanMethodSpec(string text, string name, IReadOnlyList<BeanParameterToken>? parameters)
    {
        Text = text;
        Name = name;
        Parameters = parameters;
    }

    public string Text { get; }

    public string Name { get; }

    /// <summary>The parameters, or null when the specification has no parentheses.</summary>
    public IReadOnlyList<BeanParameterToken>? Parameters { get; }

    /// <summary>Whether all parameters are types (the overload is chosen by type and parameters are bound as usual).</summary>
    public bool IsTypeSelection => Parameters is { Count: > 0 } p && p.All(t => t.IsTypeToken);

    /// <exception cref="ArgumentException">Invalid syntax.</exception>
    public static BeanMethodSpec Parse(string text)
    {
        var t = text.Trim();
        var open = t.IndexOf('(', StringComparison.Ordinal);
        if (open < 0)
        {
            if (t.Length == 0)
            {
                throw new ArgumentException("Empty method name");
            }

            return new(t, t, null);
        }

        if (!t.EndsWith(')'))
        {
            throw new ArgumentException($"Method should end with parenthesis, was: {t}");
        }

        var name = t[..open].Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException($"Missing method name in: {t}");
        }

        var tokens = Split(t[(open + 1)..^1], t).Select(BeanParameterToken.Parse).ToList();
        if (tokens.Any(p => p.IsTypeToken) && !tokens.All(p => p.IsTypeToken))
        {
            tokens = tokens.Select(p => p.AsLiteral()).ToList();
        }

        return new(t, name, tokens);
    }

    public override string ToString() => Text;

    private static List<string> Split(string parameters, string text)
    {
        var result = new List<string>();
        if (parameters.Trim().Length == 0)
        {
            return result;
        }

        var depth = 0;
        char? quote = null;
        var start = 0;
        for (var i = 0; i < parameters.Length; i++)
        {
            var c = parameters[i];
            if (quote is { } q)
            {
                if (c == q)
                {
                    quote = null;
                }

                continue;
            }

            switch (c)
            {
                case '\'' or '"':
                    quote = c;
                    break;
                case '(' or '{' or '[':
                    depth++;
                    break;
                case ')' or '}' or ']':
                    depth--;
                    break;
                case ',' when depth == 0:
                    result.Add(parameters[start..i]);
                    start = i + 1;
                    break;
            }
        }

        if (quote is not null || depth != 0)
        {
            throw new ArgumentException($"Unbalanced quotes or parentheses in method: {text}");
        }

        result.Add(parameters[start..]);
        return result;
    }
}
