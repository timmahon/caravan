namespace Caravan.Language.Simple;

/// <summary>
/// Camel's OGNL-lite navigation on function values (<c>${body.Address.City}</c>, <c>${header.order?.Lines[0].Sku}</c>,
/// <c>${body.ToUpper()}</c>, <c>${body[last]}</c>, <c>${body['key']}</c>).
/// </summary>
internal static class OgnlPath
{
    /// <summary>Whether <paramref name="text"/> continues with an OGNL step.</summary>
    public static bool StartsStep(string text) =>
        text.Length > 0 && (text[0] is '.' or '[' || (text.Length > 1 && text[0] == '?' && text[1] is '.' or '['));

    /// <summary>Parses the steps of <paramref name="path"/>, which starts at <paramref name="index"/> in <paramref name="expression"/>.</summary>
    /// <exception cref="SimpleParseException">The path is malformed.</exception>
    public static OgnlStep[] Parse(string path, int index, string expression)
    {
        var steps = new List<OgnlStep>();
        var i = 0;
        while (i < path.Length)
        {
            var nullSafe = false;
            if (path[i] == '?' && i + 1 < path.Length && path[i + 1] is '.' or '[')
            {
                nullSafe = true;
                i++;
            }

            if (path[i] == '[')
            {
                var end = SimpleParser.FindClosing(path, i, '[', ']');
                if (end < 0)
                {
                    throw new SimpleParseException("Unclosed [ in OGNL expression", expression, index + i);
                }

                var key = SimpleParser.Unquote(path[(i + 1)..end].Trim());
                if (key.Length == 0)
                {
                    throw new SimpleParseException("Empty [] in OGNL expression", expression, index + i);
                }

                steps.Add(new OgnlStep(OgnlStepKind.Index, key, [], nullSafe));
                i = end + 1;
                continue;
            }

            if (path[i] != '.')
            {
                throw new SimpleParseException($"Invalid OGNL expression '{path}': expected . or [ but was '{path[i]}'", expression, index + i);
            }

            i++;
            var start = i;
            while (i < path.Length && (char.IsLetterOrDigit(path[i]) || path[i] == '_'))
            {
                i++;
            }

            if (i == start)
            {
                throw new SimpleParseException($"Invalid OGNL expression '{path}': expected a property or method name", expression, index + i);
            }

            var name = path[start..i];
            if (i < path.Length && path[i] == '(')
            {
                var end = SimpleParser.FindClosing(path, i, '(', ')');
                if (end < 0)
                {
                    throw new SimpleParseException($"Unclosed ( in OGNL method call {name}", expression, index + i);
                }

                var args = SimpleParser.SplitArguments(path[(i + 1)..end])
                    .Select(a => SimpleParser.ParseConstant(a))
                    .ToArray();
                steps.Add(new OgnlStep(OgnlStepKind.Method, name, args, nullSafe));
                i = end + 1;
            }
            else
            {
                steps.Add(new OgnlStep(OgnlStepKind.Property, name, [], nullSafe));
            }
        }

        return [.. steps];
    }

    /// <summary>
    /// Applies <paramref name="steps"/> to <paramref name="value"/>. A null root yields null; a null reached later fails unless the
    /// next step is null-safe (<c>?.</c>), as in Camel.
    /// </summary>
    /// <exception cref="ExpressionEvaluationException">A member is missing, a value is null without <c>?.</c>, an index is out of range, or a method threw.</exception>
    public static object? Evaluate(object? value, IReadOnlyList<OgnlStep> steps, Exchange exchange, string text)
    {
        if (value is null)
        {
            return null;
        }

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (value is null)
            {
                if (step.NullSafe)
                {
                    return null;
                }

                throw new ExpressionEvaluationException(
                    text,
                    $"Failed to evaluate ${{{text}}}: the value before '{step}' is null (use ?. for null-safe navigation)");
            }

            value = step.Kind switch
            {
                OgnlStepKind.Property => MemberInvoker.GetMember(value, step.Name, text),
                OgnlStepKind.Method => MemberInvoker.Invoke(value, step.Name, step.Arguments, exchange, text),
                _ => MemberInvoker.Index(value, step.Name, step.NullSafe, exchange, text),
            };
        }

        return value;
    }
}
