using System.Text;

namespace Caravan.Support;

/// <summary>
/// Interim <c>${...}</c> substitution used by <c>toD</c>, <c>wireTap</c> and the <c>log</c> EIP until the Simple
/// language exists. Supported tokens: <c>${body}</c>, <c>${header.x}</c>, <c>${headers.x}</c>, <c>${header[x]}</c>,
/// <c>${exchangeProperty.x}</c>, <c>${exchangeProperty[x]}</c>, <c>${exchangeId}</c>, <c>${id}</c>/<c>${messageId}</c>,
/// <c>${routeId}</c>, <c>${exception.message}</c>; an <c>in.</c> prefix (<c>${in.body}</c>) is accepted.
/// Null values render as empty strings. <c>{{placeholders}}</c> are left as is.
/// </summary>
public static class TokenTemplate
{
    /// <summary>Whether <paramref name="template"/> contains a <c>${...}</c> token.</summary>
    public static bool HasTokens(string template) => template.Contains("${", StringComparison.Ordinal);

    /// <summary>Parses <paramref name="template"/> into an expression that evaluates to the substituted string.</summary>
    /// <exception cref="ArgumentException">The template has an unknown or unterminated token.</exception>
    public static IExpression Parse(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (!HasTokens(template))
        {
            return new FuncExpression(_ => template, template);
        }

        var parts = new List<Func<Exchange, object?>>();
        var i = 0;
        while (i < template.Length)
        {
            var start = template.IndexOf("${", i, StringComparison.Ordinal);
            if (start < 0)
            {
                var tail = template[i..];
                parts.Add(_ => tail);
                break;
            }

            if (start > i)
            {
                var literal = template[i..start];
                parts.Add(_ => literal);
            }

            var end = template.IndexOf('}', start + 2);
            if (end < 0)
            {
                throw new ArgumentException($"Unterminated token at position {start} in: {template}", nameof(template));
            }

            parts.Add(ParseToken(template[(start + 2)..end].Trim(), template));
            i = end + 1;
        }

        return new FuncExpression(
            e =>
            {
                var sb = new StringBuilder();
                foreach (var part in parts)
                {
                    sb.Append(ObjectHelper.ToText(e, part(e)));
                }

                return sb.ToString();
            },
            template);
    }

    private static Func<Exchange, object?> ParseToken(string token, string template)
    {
        if (token.StartsWith("in.", StringComparison.Ordinal))
        {
            token = token[3..];
        }

        switch (token)
        {
            case "body":
                return e => e.Message.Body;
            case "exchangeId":
                return e => e.ExchangeId;
            case "id" or "messageId":
                return e => e.Message.MessageId;
            case "routeId":
                return e => e.CurrentRouteId ?? e.FromRouteId;
            case "exception.message":
                return e => (e.Exception ?? e.GetProperty(ExchangePropertyKeys.ExceptionCaught) as Exception)?.Message;
        }

        var header = Named(token, "header") ?? Named(token, "headers");
        if (header is not null)
        {
            return e => e.Message.GetHeader(header);
        }

        if (Named(token, "exchangeProperty") is { } property)
        {
            return e => e.GetProperty(property);
        }

        throw new ArgumentException($"Unknown token '${{{token}}}' in: {template}", nameof(template));
    }

    private static string? Named(string token, string prefix)
    {
        if (token.Length > prefix.Length + 1 && token.StartsWith(prefix, StringComparison.Ordinal))
        {
            var rest = token[prefix.Length..];
            if (rest[0] == '.')
            {
                return rest[1..];
            }

            if (rest[0] == '[' && rest[^1] == ']')
            {
                return rest[1..^1].Trim('\'', '"');
            }
        }

        return null;
    }
}
