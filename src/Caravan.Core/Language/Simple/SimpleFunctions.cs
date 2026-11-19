using System.Globalization;
using System.Reflection;
using Caravan.Support;

namespace Caravan.Language.Simple;

/// <summary>Compiles the text of one Simple function (the part between <c>${</c> and <c>}</c>) into a node.</summary>
internal static class SimpleFunctions
{
    /// <summary>Creates the node for <paramref name="function"/>, found at <paramref name="index"/> in <paramref name="expression"/>.</summary>
    /// <exception cref="SimpleParseException">Unknown function or invalid syntax.</exception>
    public static SimpleNode Create(string function, int index, string expression)
    {
        var f = function.Trim();
        if (f.Length == 0)
        {
            throw new SimpleParseException("Empty function", expression, index);
        }

        foreach (var (prefix, factory) in SimpleLanguage.CustomFunctions)
        {
            if (f.StartsWith(prefix + ":", StringComparison.Ordinal))
            {
                IExpression custom;
                try
                {
                    custom = factory(f[(prefix.Length + 1)..]);
                }
                catch (Exception e) when (e is not SimpleParseException)
                {
                    throw new SimpleParseException($"Invalid {prefix}: function: {e.Message}", expression, index, e);
                }

                return new DelegateNode(custom.Evaluate, f);
            }
        }

        return CreateMessageFunction(f, index, expression)
               ?? CreateExchangeFunction(f, index, expression)
               ?? CreateMiscFunction(f, index, expression)
               ?? throw new SimpleParseException($"Unknown function: {f}", expression, index);
    }

    private static SimpleNode? CreateMessageFunction(string f, int index, string expression)
    {
        var g = f.StartsWith("in.body", StringComparison.Ordinal) || f.StartsWith("in.header", StringComparison.Ordinal) ? f[3..] : f;
        switch (g)
        {
            case "body":
                return new DelegateNode(e => e.Message.Body, f, (e, v) => e.Message.Body = v);
            case "bodyType":
                return new DelegateNode(e => e.Message.Body?.GetType(), f);
            case "bodyOneLine":
                return new DelegateNode(e => ObjectHelper.ToText(e, e.Message.Body)?.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal), f);
            case "headers":
                return new DelegateNode(e => e.Message.Headers, f);
        }

        if (g.StartsWith("body", StringComparison.Ordinal) && OgnlPath.StartsStep(g[4..]))
        {
            var steps = OgnlPath.Parse(g[4..], index + (f.Length - g.Length) + 4, expression);
            return new DelegateNode(e => OgnlPath.Evaluate(e.Message.Body, steps, e, f), f);
        }

        if (TryCall(g, "bodyAs", out var bodyArgs, out var bodyRest) || TryCall(g, "mandatoryBodyAs", out bodyArgs, out bodyRest))
        {
            var mandatory = g.StartsWith("mandatory", StringComparison.Ordinal);
            if (bodyArgs.Count != 1)
            {
                throw new SimpleParseException($"Valid syntax: ${{{(mandatory ? "mandatoryBodyAs" : "bodyAs")}(type)}} was: {f}", expression, index);
            }

            var type = ResolveType(bodyArgs[0], f, index, expression);
            var steps = ParseRest(bodyRest, f, index, expression);
            return new DelegateNode(
                e =>
                {
                    var body = e.Message.Body;
                    object? value;
                    if (mandatory)
                    {
                        try
                        {
                            value = body is null ? null : e.Context.TypeConverter.Convert(type.Value, e, body);
                        }
                        catch (TypeConversion.NoTypeConversionAvailableException ex)
                        {
                            throw new InvalidPayloadException(e, type.Value, ex);
                        }

                        if (value is null)
                        {
                            throw new InvalidPayloadException(e, type.Value);
                        }
                    }
                    else
                    {
                        value = e.Context.TypeConverter.TryConvert(type.Value, e, body, out var converted) ? converted : null;
                    }

                    return OgnlPath.Evaluate(value, steps, e, f);
                },
                f);
        }

        if (TryCall(g, "headerAs", out var headerArgs, out var headerRest) && headerRest.Length == 0)
        {
            if (headerArgs.Count != 2)
            {
                throw new SimpleParseException($"Valid syntax: ${{headerAs(key,type)}} was: {f}", expression, index);
            }

            var key = SimpleParser.Unquote(headerArgs[0]);
            var type = ResolveType(headerArgs[1], f, index, expression);
            return new DelegateNode(e => e.Message.GetHeader(key) is { } v ? e.Context.TypeConverter.Convert(type.Value, e, v) : null, f);
        }

        foreach (var prefix in (string[])["headers", "header"])
        {
            if (g.StartsWith(prefix, StringComparison.Ordinal) && g.Length > prefix.Length && g[prefix.Length] is '.' or ':' or '[')
            {
                return CreateKeyed(f, g[prefix.Length..], prefix, index, index + (f.Length - g.Length) + prefix.Length, expression,
                    (e, k) => e.Message.GetHeader(k), (e, k, v) => e.Message.SetHeader(k, v));
            }
        }

        return null;
    }

    private static SimpleNode? CreateExchangeFunction(string f, int index, string expression)
    {
        switch (f)
        {
            case "exchangeId":
                return new DelegateNode(e => e.ExchangeId, f);
            case "id" or "messageId":
                return new DelegateNode(e => e.Message.MessageId, f);
            case "routeId":
                return new DelegateNode(e => e.CurrentRouteId ?? e.FromRouteId, f);
            case "fromRouteId":
                return new DelegateNode(e => e.FromRouteId, f);
            case "camelId" or "contextName" or "caravanId":
                return new DelegateNode(e => e.Context.Name, f);
            case "stepId":
                return new DelegateNode(e => e.GetProperty(ExchangePropertyKeys.StepId), f);
            case "exchange":
                return new DelegateNode(e => e, f);
            case "exception":
                return new DelegateNode(GetException, f);
            case "exception.stacktrace" or "exception.stackTrace":
                return new DelegateNode(e => GetException(e)?.ToString(), f);
        }

        if (f.StartsWith("exchange", StringComparison.Ordinal) && OgnlPath.StartsStep(f[8..]))
        {
            var steps = OgnlPath.Parse(f[8..], index + 8, expression);
            return new DelegateNode(e => OgnlPath.Evaluate(e, steps, e, f), f);
        }

        if (f.StartsWith("exception", StringComparison.Ordinal) && OgnlPath.StartsStep(f[9..]))
        {
            var steps = OgnlPath.Parse(f[9..], index + 9, expression);
            return new DelegateNode(e => OgnlPath.Evaluate(GetException(e), steps, e, f), f);
        }

        if (TryCall(f, "exchangePropertyAs", out var args, out var rest) && rest.Length == 0)
        {
            if (args.Count != 2)
            {
                throw new SimpleParseException($"Valid syntax: ${{exchangePropertyAs(key,type)}} was: {f}", expression, index);
            }

            var key = SimpleParser.Unquote(args[0]);
            var type = ResolveType(args[1], f, index, expression);
            return new DelegateNode(e => e.GetProperty(key) is { } v ? e.Context.TypeConverter.Convert(type.Value, e, v) : null, f);
        }

        if (f.StartsWith("exchangeProperty", StringComparison.Ordinal) && f.Length > 16 && f[16] is '.' or ':' or '[')
        {
            return CreateKeyed(f, f[16..], "exchangeProperty", index, index + 16, expression, (e, k) => e.GetProperty(k), (e, k, v) => e.SetProperty(k, v));
        }

        if (f.StartsWith("date:", StringComparison.Ordinal) || f.StartsWith("date-with-timezone:", StringComparison.Ordinal))
        {
            return SimpleDateFunction.Create(f, index, expression);
        }

        return null;
    }

    private static SimpleNode? CreateMiscFunction(string f, int index, string expression)
    {
        switch (f)
        {
            case "null":
                return new LiteralNode(null);
            case "uuid" or "uuid:default" or "uuid:classic" or "uuid:random":
                return new DelegateNode(_ => Guid.NewGuid().ToString(), f);
            case "uuid:simple":
                return new DelegateNode(_ => Guid.NewGuid().ToString("N"), f);
            case "threadName":
                return new DelegateNode(_ => Thread.CurrentThread.Name ?? $"thread-{Environment.CurrentManagedThreadId}", f);
            case "threadId":
                return new DelegateNode(_ => Environment.CurrentManagedThreadId, f);
            case "hostname":
                return new DelegateNode(_ => Environment.MachineName, f);
        }

        if (f.StartsWith("uuid:", StringComparison.Ordinal))
        {
            throw new SimpleParseException($"Unknown uuid generator: {f[5..]} (supported: default, classic, random, simple)", expression, index);
        }

        if (After(f, "properties:") is { } property)
        {
            var colon = property.IndexOf(':', StringComparison.Ordinal);
            var key = (colon < 0 ? property : property[..colon]).Trim();
            var fallback = colon < 0 ? null : property[(colon + 1)..];
            if (key.Length == 0)
            {
                throw new SimpleParseException($"Valid syntax: ${{properties:key[:default]}} was: {f}", expression, index);
            }

            return new DelegateNode(
                e => e.Context.PropertiesComponent.ResolveProperty(key)
                     ?? (fallback is null ? throw new ExpressionEvaluationException(f, $"Property with key [{key}] not found in properties from text: ${{{f}}}") : e.Context.PropertiesComponent.ResolvePlaceholders(fallback)),
                f);
        }

        if (After(f, "propertiesExist:") is { } exist)
        {
            var negate = exist.StartsWith('!');
            var key = (negate ? exist[1..] : exist).Trim();
            return new DelegateNode(e => (e.Context.PropertiesComponent.ResolveProperty(key) is not null) != negate, f);
        }

        if ((After(f, "sys.") ?? After(f, "sys:")) is { } sys)
        {
            return new DelegateNode(_ => AppContext.GetData(sys)?.ToString(), f);
        }

        if ((After(f, "sysenv.") ?? After(f, "sysenv:") ?? After(f, "env.") ?? After(f, "env:")) is { } env)
        {
            return new DelegateNode(_ => Environment.GetEnvironmentVariable(env) ?? Environment.GetEnvironmentVariable(env.ToUpperInvariant()), f);
        }

        if (After(f, "ref:") is { } beanName)
        {
            return new DelegateNode(e => e.Context.Registry.Lookup(beanName), f);
        }

        if (After(f, "type:") is { } typeField)
        {
            return CreateTypeField(f, typeField, index, expression);
        }

        if (f.StartsWith("bean:", StringComparison.Ordinal))
        {
            throw new SimpleParseException(
                "The bean: function is not available yet (it comes with the bean component); register one with SimpleLanguage.RegisterFunction(\"bean\", ...)",
                expression,
                index);
        }

        if (TryCall(f, "random", out var args, out var rest) && rest.Length == 0)
        {
            var numbers = args.Select(a => int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : (int?)null).ToList();
            if (numbers.Count is < 1 or > 2 || numbers.Any(n => n is null))
            {
                throw new SimpleParseException($"Valid syntax: ${{random(min,max)}} or ${{random(max)}} was: {f}", expression, index);
            }

            var min = numbers.Count == 2 ? numbers[0]!.Value : 0;
            var max = numbers[^1]!.Value;
            if (max <= min)
            {
                throw new SimpleParseException($"random(min,max): max must be greater than min in: {f}", expression, index);
            }

            return new DelegateNode(_ => Random.Shared.Next(min, max), f);
        }

        if (TryCall(f, "empty", out args, out rest) && rest.Length == 0)
        {
            Func<object>? create = args.Count != 1 ? null : SimpleParser.Unquote(args[0]).ToLowerInvariant() switch
            {
                "string" => () => string.Empty,
                "list" => () => new List<object?>(),
                "map" => () => new Dictionary<string, object?>(),
                "set" => () => new HashSet<object?>(),
                _ => null,
            };
            if (create is null)
            {
                throw new SimpleParseException($"Valid syntax: ${{empty(string|list|map|set)}} was: {f}", expression, index);
            }

            return new DelegateNode(_ => create(), f);
        }

        if ((TryCall(f, "collate", out args, out rest) || TryCall(f, "skip", out args, out rest)) && rest.Length == 0)
        {
            var collate = f.StartsWith("collate", StringComparison.Ordinal);
            if (args.Count != 1 || !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < (collate ? 1 : 0))
            {
                throw new SimpleParseException($"Valid syntax: ${{{(collate ? "collate" : "skip")}(number)}} was: {f}", expression, index);
            }

            return collate
                ? new DelegateNode(e => SimpleIterables.Collate(SimpleIterables.Items(e.Message.Body), n), f)
                : new DelegateNode(e => SimpleIterables.Items(e.Message.Body).Skip(n), f);
        }

        return null;
    }

    /// <summary><c>join(separator, prefix, source)</c>: the source items (default the body) joined; the source may be a nested function.</summary>
    public static SimpleNode CreateJoin(string f, IReadOnlyList<string> args, Func<string, SimpleNode> parseSource, int index, string expression)
    {
        if (args.Count > 3)
        {
            throw new SimpleParseException($"Valid syntax: ${{join(separator,prefix,exp)}} was: {f}", expression, index);
        }

        var separator = args.Count > 0 && args[0].Length > 0 ? SimpleParser.Unquote(args[0]) : ",";
        var prefix = args.Count > 1 ? SimpleParser.Unquote(args[1]) : string.Empty;
        var source = args.Count > 2 && args[2].Length > 0 ? parseSource(args[2]) : null;
        return new DelegateNode(
            e => string.Join(
                separator,
                SimpleIterables.Items(source is null ? e.Message.Body : source.Evaluate(e)).Select(item => prefix + ObjectHelper.ToText(e, item))),
            f);
    }

    private static SimpleNode CreateKeyed(
        string f,
        string remainder,
        string prefix,
        int index,
        int remainderIndex,
        string expression,
        Func<Exchange, string, object?> get,
        Action<Exchange, string, object?> set)
    {
        string key;
        OgnlStep[] steps;
        string? fullName = null;
        if (remainder[0] == '[')
        {
            var end = SimpleParser.FindClosing(remainder, 0, '[', ']');
            if (end < 0)
            {
                throw new SimpleParseException($"Valid syntax: ${{{prefix}[name]}} was: {f}", expression, index);
            }

            key = SimpleParser.Unquote(remainder[1..end].Trim());
            steps = OgnlPath.Parse(remainder[(end + 1)..], remainderIndex + end + 1, expression);
        }
        else
        {
            var name = remainder[1..];
            var stepStart = FindOgnlStart(name);
            key = SimpleParser.Unquote(stepStart < 0 ? name : name[..stepStart]);
            steps = stepStart < 0 ? [] : OgnlPath.Parse(name[stepStart..], remainderIndex + 1 + stepStart, expression);
            if (steps.Length > 0)
            {
                // As Camel: a key may contain dots, so the full name is tried first.
                fullName = name;
            }
        }

        if (key.Length == 0)
        {
            throw new SimpleParseException($"Valid syntax: ${{{prefix}.name}} was: {f}", expression, index);
        }

        if (steps.Length == 0)
        {
            return new DelegateNode(e => get(e, key), f, (e, v) => set(e, key, v));
        }

        return new DelegateNode(
            e =>
            {
                if (fullName is not null && get(e, fullName) is { } direct)
                {
                    return direct;
                }

                return OgnlPath.Evaluate(get(e, key), steps, e, f);
            },
            f);
    }

    private static int FindOgnlStart(string name)
    {
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] is '.' or '[' || (name[i] == '?' && i + 1 < name.Length && name[i + 1] is '.' or '['))
            {
                return i;
            }
        }

        return -1;
    }

    private static SimpleNode CreateTypeField(string f, string typeField, int index, string expression)
    {
        var dot = typeField.LastIndexOf('.');
        if (dot <= 0 || dot == typeField.Length - 1)
        {
            throw new SimpleParseException($"Valid syntax: ${{type:Full.Type.Name.FIELD}} was: {f}", expression, index);
        }

        var typeName = typeField[..dot].Trim();
        var member = typeField[(dot + 1)..].Trim();
        var value = new Lazy<object?>(() =>
        {
            var type = TypeNameResolver.Resolve(typeName)
                ?? throw new ExpressionEvaluationException(f, $"Cannot resolve type '{typeName}' in ${{{f}}}");
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            if (type.GetField(member, Flags) is { } field)
            {
                return field.GetValue(null);
            }

            if (type.GetProperty(member, Flags) is { } property)
            {
                return property.GetValue(null);
            }

            throw new ExpressionEvaluationException(f, $"No public static field or property '{member}' on {type.FullName} in ${{{f}}}");
        });
        return new DelegateNode(_ => value.Value, f);
    }

    private static Lazy<Type> ResolveType(string name, string f, int index, string expression)
    {
        var typeName = SimpleParser.Unquote(name.Trim());
        if (typeName.Length == 0)
        {
            throw new SimpleParseException($"Missing type name in: {f}", expression, index);
        }

        if (TypeNameResolver.Resolve(typeName) is { } type)
        {
            return new Lazy<Type>(type);
        }

        // The type's assembly may not be loaded yet: resolve again when evaluated.
        return new Lazy<Type>(() => TypeNameResolver.Resolve(typeName)
            ?? throw new ExpressionEvaluationException(f, $"Cannot resolve type '{typeName}' in ${{{f}}}"));
    }

    private static OgnlStep[] ParseRest(string rest, string f, int index, string expression)
    {
        if (rest.Length == 0)
        {
            return [];
        }

        if (!OgnlPath.StartsStep(rest))
        {
            throw new SimpleParseException($"Unexpected text '{rest}' in: {f}", expression, index);
        }

        return OgnlPath.Parse(rest, index + f.Length - rest.Length, expression);
    }

    /// <summary>Matches <c>name(args)rest</c>; the arguments are split at top-level commas and trimmed.</summary>
    internal static bool TryCall(string f, string name, out IReadOnlyList<string> args, out string rest)
    {
        args = [];
        rest = string.Empty;
        if (!f.StartsWith(name + "(", StringComparison.Ordinal))
        {
            return false;
        }

        var end = SimpleParser.FindClosing(f, name.Length, '(', ')');
        if (end < 0)
        {
            return false;
        }

        args = SimpleParser.SplitArguments(f[(name.Length + 1)..end]);
        rest = f[(end + 1)..];
        return true;
    }

    private static string? After(string f, string prefix) =>
        f.Length > prefix.Length && f.StartsWith(prefix, StringComparison.Ordinal) ? f[prefix.Length..] : null;

    private static Exception? GetException(Exchange e) =>
        e.Exception ?? e.GetProperty(ExchangePropertyKeys.ExceptionCaught) as Exception;
}
