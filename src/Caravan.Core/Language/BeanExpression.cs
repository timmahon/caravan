using Caravan.Components.Bean;
using Caravan.Processors;

namespace Caravan.Language;

/// <summary>
/// An expression whose value is the result of a bean method (Camel's <c>BeanExpression</c>, the <c>bean</c>/<c>method</c> language,
/// <see cref="Expr.Method(string, string?)"/> and the Simple <c>${bean:...}</c> function). The method is chosen and its parameters bound as by
/// the <c>bean</c> EIP, on the exchange being evaluated; the body is not changed (a <c>void</c> method yields null). Asynchronous methods
/// are waited for synchronously, as expressions are synchronous.
/// </summary>
public sealed class BeanExpression : IExpression
{
    private readonly BeanHolder _holder;
    private readonly string _text;
    private readonly BeanHolder? _dottedHolder;
    private readonly string? _dottedMethod;

    internal BeanExpression(BeanHolder holder, string? method, string text, BeanHolder? dottedHolder = null, string? dottedMethod = null)
    {
        _holder = holder;
        Method = string.IsNullOrWhiteSpace(method) ? null : method;
        _text = text;
        _dottedHolder = dottedHolder;
        _dottedMethod = dottedMethod;
        if (Method is not null)
        {
            // Fails early on a malformed method specification.
            BeanMethodSpec.Parse(Method);
        }
    }

    /// <summary>The method specification, or null to choose by the body.</summary>
    public string? Method { get; }

    /// <summary>
    /// Parses the text of the <c>bean</c> language: <c>beanName</c>, <c>beanName?method=foo</c> (more options: <c>scope=Request</c>,
    /// <c>bean.property=value</c>), <c>beanName.foo</c> (when no bean is named <c>beanName.foo</c>), or <c>class:Full.Type.Name?method=foo</c>
    /// (also <c>#class:</c>; a static class's static method or an instance created as by <c>.Bean(type)</c>).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The expression.</returns>
    /// <exception cref="ArgumentException">The text is invalid or the class cannot be found.</exception>
    public static BeanExpression Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var t = text.Trim();
        var query = t.IndexOf('?', StringComparison.Ordinal);
        var target = query < 0 ? t : t[..query];
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (query >= 0)
        {
            foreach (var pair in SplitQuery(t[(query + 1)..]))
            {
                var eq = pair.IndexOf('=', StringComparison.Ordinal);
                if (eq <= 0)
                {
                    throw new ArgumentException($"Invalid bean option '{pair}' in: {t}");
                }

                options[pair[..eq]] = Uri.UnescapeDataString(pair[(eq + 1)..]);
            }
        }

        options.Remove("method", out var method);
        var scope = options.Remove("scope", out var scopeText) ? Enum.Parse<BeanScope>(scopeText, ignoreCase: true) : BeanScope.Singleton;
        var beanOptions = options.Where(o => o.Key.StartsWith("bean.", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(o => o.Key[5..], o => o.Value, StringComparer.OrdinalIgnoreCase);
        var unknown = options.Keys.Where(k => !k.StartsWith("bean.", StringComparison.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0)
        {
            throw new ArgumentException($"Unknown bean options: {string.Join(", ", unknown)} in: {t}");
        }

        if (target.StartsWith("#class:", StringComparison.Ordinal) || target.StartsWith("class:", StringComparison.Ordinal))
        {
            var typeName = target[(target.IndexOf(':', StringComparison.Ordinal) + 1)..];
            var type = Simple.TypeNameResolver.Resolve(typeName) ?? throw new ArgumentException($"Cannot find class: {typeName}");
            return new BeanExpression(BeanHolder.ForType(type, scope, beanOptions), method, t);
        }

        var dot = target.IndexOf('.', StringComparison.Ordinal);
        if (method is null && dot > 0 && dot < target.Length - 1)
        {
            return new BeanExpression(
                BeanHolder.ForName(target, scope, beanOptions),
                null,
                t,
                BeanHolder.ForName(target[..dot], scope, beanOptions),
                target[(dot + 1)..]);
        }

        return new BeanExpression(BeanHolder.ForName(target, scope, beanOptions), method, t);
    }

    /// <inheritdoc />
    public object? Evaluate(Exchange exchange)
    {
        var (holder, method) = _dottedHolder is not null && exchange.Context.Registry.Lookup(exchange.Context.ResolvePropertyPlaceholders(_holder.Name!)) is null
            ? (_dottedHolder, _dottedMethod)
            : (_holder, Method);
        var (_, result) = BeanProcessor.InvokeAsync(holder, method, exchange).AsTask().GetAwaiter().GetResult();
        return result;
    }

    /// <inheritdoc />
    public override string ToString() => $"bean:{_text}";

    private static IEnumerable<string> SplitQuery(string query)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < query.Length; i++)
        {
            switch (query[i])
            {
                case '(' or '{':
                    depth++;
                    break;
                case ')' or '}':
                    depth--;
                    break;
                case '&' when depth == 0:
                    yield return query[start..i];
                    start = i + 1;
                    break;
            }
        }

        if (start < query.Length)
        {
            yield return query[start..];
        }
    }
}
