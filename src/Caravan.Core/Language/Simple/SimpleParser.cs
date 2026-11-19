using System.Globalization;
using System.Text;

namespace Caravan.Language.Simple;

/// <summary>
/// Parses Simple text into <see cref="SimpleNode"/>s. Three entry points: expressions (text with <c>${...}</c> functions),
/// predicates (operands joined by whitespace-separated binary and logical operators) and function contents (a function, or
/// function operands with operators, <c>?</c>/<c>:</c> and <c>?:</c>).
/// </summary>
internal sealed class SimpleParser
{
    private static readonly Dictionary<string, BinaryOperator> BinaryOperators = new(StringComparer.Ordinal)
    {
        ["=="] = BinaryOperator.Equal,
        ["=~"] = BinaryOperator.EqualIgnoreCase,
        ["!="] = BinaryOperator.NotEqual,
        ["!=~"] = BinaryOperator.NotEqualIgnoreCase,
        [">"] = BinaryOperator.GreaterThan,
        [">="] = BinaryOperator.GreaterThanOrEqual,
        ["<"] = BinaryOperator.LessThan,
        ["<="] = BinaryOperator.LessThanOrEqual,
        ["contains"] = BinaryOperator.Contains,
        ["!contains"] = BinaryOperator.NotContains,
        ["~~"] = BinaryOperator.ContainsIgnoreCase,
        ["!~~"] = BinaryOperator.NotContainsIgnoreCase,
        ["regex"] = BinaryOperator.Regex,
        ["!regex"] = BinaryOperator.NotRegex,
        ["in"] = BinaryOperator.In,
        ["!in"] = BinaryOperator.NotIn,
        ["range"] = BinaryOperator.Range,
        ["!range"] = BinaryOperator.NotRange,
        ["is"] = BinaryOperator.Is,
        ["!is"] = BinaryOperator.NotIs,
        ["startsWith"] = BinaryOperator.StartsWith,
        ["!startsWith"] = BinaryOperator.NotStartsWith,
        ["endsWith"] = BinaryOperator.EndsWith,
        ["!endsWith"] = BinaryOperator.NotEndsWith,
    };

    private readonly string _expression;

    private SimpleParser(string expression)
    {
        _expression = expression;
    }

    /// <summary>Parses an expression: literal text and functions; a lone function keeps its value's type.</summary>
    public static SimpleNode ParseExpression(string text) => new SimpleParser(text).Template(text, 0, allowIncDec: true);

    /// <summary>Parses a predicate.</summary>
    public static SimpleNode ParsePredicate(string text)
    {
        var parser = new SimpleParser(text);
        var words = parser.SplitWords(text, 0);
        if (words.Count == 0)
        {
            throw new SimpleParseException("Empty predicate", text, 0);
        }

        return parser.ParseWords(words, functionMode: false);
    }

    /// <summary>Parses the content of a function (the text between <c>${</c> and <c>}</c>).</summary>
    public static SimpleNode ParseFunction(string content) => new SimpleParser(content).FunctionContent(content, 0);

    /// <summary>The index of the <paramref name="close"/> matching the <paramref name="open"/> at <paramref name="start"/>, skipping quoted text; -1 when unclosed.</summary>
    public static int FindClosing(string text, int start, char open, char close)
    {
        var depth = 0;
        var quote = '\0';
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c is '\'' or '"')
            {
                quote = c;
            }
            else if (c == open)
            {
                depth++;
            }
            else if (c == close && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Splits function arguments at top-level commas (outside quotes, brackets and nested functions), trimmed.</summary>
    public static IReadOnlyList<string> SplitArguments(string text)
    {
        var result = new List<string>();
        if (text.Trim().Length == 0)
        {
            return result;
        }

        var depth = 0;
        var quote = '\0';
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            switch (c)
            {
                case '\'' or '"':
                    quote = c;
                    break;
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    result.Add(text[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }

        result.Add(text[start..].Trim());
        return result;
    }

    /// <summary>The text between matching quotes, or the text itself when it is not quoted.</summary>
    public static string Unquote(string text) => IsQuoted(text) ? text[1..^1] : text;

    /// <summary>A constant argument: quoted text, <c>null</c>, <c>true</c>/<c>false</c>, a number, else the text itself.</summary>
    public static object? ParseConstant(string text) => text switch
    {
        _ when IsQuoted(text) => text[1..^1],
        "null" => null,
        "true" => true,
        "false" => false,
        _ => ParseNumber(text) ?? text,
    };

    /// <summary>An <see cref="int"/>, <see cref="long"/> or <see cref="double"/> literal, or null when <paramref name="text"/> is not a number.</summary>
    public static object? ParseNumber(string text)
    {
        if (text.Length == 0 || !(char.IsAsciiDigit(text[0]) || (text[0] == '-' && text.Length > 1 && char.IsAsciiDigit(text[1]))))
        {
            return null;
        }

        if (int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var i))
        {
            return i;
        }

        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l))
        {
            return l;
        }

        return double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d)
            ? d
            : null;
    }

    private static bool IsQuoted(string text)
    {
        if (text.Length < 2 || text[0] is not ('\'' or '"') || text[^1] != text[0])
        {
            return false;
        }

        for (var i = 1; i < text.Length; i++)
        {
            if (text[i] == '\\')
            {
                i++;
            }
            else if (text[i] == text[0])
            {
                return i == text.Length - 1;
            }
        }

        return false;
    }

    private static int FunctionStart(string text, int i)
    {
        if (text[i] != '$' || i + 1 >= text.Length)
        {
            return 0;
        }

        if (text[i + 1] == '{')
        {
            return 2;
        }

        return string.CompareOrdinal(text, i + 1, "simple{", 0, 7) == 0 ? 8 : 0;
    }

    private static bool HasFunction(string text) =>
        text.Contains("${", StringComparison.Ordinal) || text.Contains("$simple{", StringComparison.Ordinal);

    private SimpleParseException Error(string reason, int index) => new(reason, _expression, index);

    private SimpleNode Template(string text, int offset, bool allowIncDec)
    {
        var parts = new List<SimpleNode>();
        var sb = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length)
            {
                var next = text[i + 1];
                switch (next)
                {
                    case 'n':
                        sb.Append('\n');
                        break;
                    case 't':
                        sb.Append('\t');
                        break;
                    case 'r':
                        sb.Append('\r');
                        break;
                    case '$' or '}' or '\'' or '"':
                        sb.Append(next);
                        break;
                    default:
                        sb.Append(c).Append(next);
                        break;
                }

                i += 2;
                continue;
            }

            var length = FunctionStart(text, i);
            if (length == 0)
            {
                sb.Append(c);
                i++;
                continue;
            }

            var end = FindFunctionEnd(text, i + length);
            if (end < 0)
            {
                throw Error("Unterminated function: expected } to close the function", offset + i);
            }

            if (sb.Length > 0)
            {
                parts.Add(new LiteralNode(sb.ToString()));
                sb.Clear();
            }

            var node = FunctionContent(text[(i + length)..end], offset + i + length);
            var start = i;
            i = end + 1;
            if (allowIncDec && i + 1 < text.Length && text[i] == text[i + 1] && text[i] is '+' or '-' &&
                (i + 2 == text.Length || char.IsWhiteSpace(text[i + 2])))
            {
                node = new IncDecNode(node, text[i] == '+' ? 1 : -1, text[start..(i + 2)]);
                i += 2;
            }

            parts.Add(node);
        }

        if (sb.Length > 0)
        {
            parts.Add(new LiteralNode(sb.ToString()));
        }

        return parts.Count switch
        {
            0 => new LiteralNode(string.Empty),
            1 => parts[0],
            _ => new TemplateNode(parts),
        };
    }

    private static int FindFunctionEnd(string text, int start)
    {
        var depth = 1;
        var quote = '\0';
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            switch (c)
            {
                case '\\' when i + 1 < text.Length:
                    i++;
                    break;
                case '\'' or '"':
                    quote = c;
                    break;
                case '{':
                    depth++;
                    break;
                case '}' when --depth == 0:
                    return i;
            }
        }

        return -1;
    }

    private SimpleNode FunctionContent(string content, int offset)
    {
        var words = SplitWords(content, offset);
        if (words.Count == 0)
        {
            throw Error("Empty function", Math.Max(0, offset - 2));
        }

        if (!words.Any(w => IsOperator(w.Text, functionMode: true)))
        {
            var trimmed = content.TrimStart();
            return FunctionText(trimmed.TrimEnd(), offset + content.Length - trimmed.Length);
        }

        return ParseWords(words, functionMode: true);
    }

    private SimpleNode FunctionText(string text, int offset)
    {
        if (SimpleFunctions.TryCall(text, "join", out var args, out var rest) && rest.Length == 0)
        {
            return SimpleFunctions.CreateJoin(
                text,
                args,
                source => HasFunction(source) ? Template(source, offset, allowIncDec: false) : FunctionText(source, offset),
                offset,
                _expression);
        }

        return HasFunction(text)
            ? new DynamicFunctionNode(Template(text, offset, allowIncDec: false))
            : SimpleFunctions.Create(text, offset, _expression);
    }

    private List<(string Text, int Index)> SplitWords(string text, int offset)
    {
        var words = new List<(string, int)>();
        var i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                i++;
                continue;
            }

            var start = i;
            var quote = '\0';
            var quoteStart = 0;
            var braces = 0;
            var brackets = 0;
            var functionStart = 0;
            while (i < text.Length)
            {
                var c = text[i];
                if (quote != '\0')
                {
                    if (c == '\\' && i + 1 < text.Length)
                    {
                        i += 2;
                        continue;
                    }

                    if (c == quote)
                    {
                        quote = '\0';
                    }

                    i++;
                    continue;
                }

                if (c == '\\' && i + 1 < text.Length)
                {
                    i += 2;
                    continue;
                }

                var length = FunctionStart(text, i);
                if (length > 0)
                {
                    if (braces == 0)
                    {
                        functionStart = i;
                    }

                    braces++;
                    i += length;
                    continue;
                }

                if (c is '\'' or '"')
                {
                    quote = c;
                    quoteStart = i;
                }
                else if (braces > 0 && c == '{')
                {
                    braces++;
                }
                else if (braces > 0 && c == '}')
                {
                    braces--;
                }
                else if (braces == 0 && c is '(' or '[')
                {
                    brackets++;
                }
                else if (braces == 0 && c is ')' or ']' && brackets > 0)
                {
                    brackets--;
                }
                else if (braces == 0 && brackets == 0 && char.IsWhiteSpace(c))
                {
                    break;
                }

                i++;
            }

            if (quote != '\0')
            {
                throw Error($"Unterminated quote: expected {quote} to close the text", offset + quoteStart);
            }

            if (braces > 0)
            {
                throw Error("Unterminated function: expected } to close the function", offset + functionStart);
            }

            words.Add((text[start..i], offset + start));
        }

        return words;
    }

    private static bool IsOperator(string word, bool functionMode) =>
        BinaryOperators.ContainsKey(word) || word is "&&" or "||" || (functionMode && word is "?" or ":" or "?:");

    private SimpleNode ParseWords(List<(string Text, int Index)> words, bool functionMode)
    {
        var position = 0;
        var node = ParseConditional(words, ref position, functionMode);
        if (position < words.Count)
        {
            var (text, index) = words[position];
            throw IsOperator(text, functionMode)
                ? Error($"Unexpected operator {text}", index)
                : Error($"Unexpected token '{text}': expected an operator", index);
        }

        return node;
    }

    private SimpleNode ParseConditional(List<(string Text, int Index)> words, ref int position, bool functionMode)
    {
        var left = ParseLogical(words, ref position, functionMode);
        if (!functionMode || position >= words.Count)
        {
            return left;
        }

        var (symbol, index) = words[position];
        if (symbol == "?:")
        {
            position++;
            RequireOperand(words, position, functionMode, $"Elvis operator {symbol} does not have a right hand side", index);
            return new ElvisNode(left, ParseConditional(words, ref position, functionMode));
        }

        if (symbol == "?")
        {
            position++;
            RequireOperand(words, position, functionMode, "Ternary operator ? does not have a value when true", index);
            var whenTrue = ParseLogical(words, ref position, functionMode);
            if (position >= words.Count || words[position].Text != ":")
            {
                throw Error("Ternary operator ? expects : and a value when false", position < words.Count ? words[position].Index : index);
            }

            var colon = words[position].Index;
            position++;
            RequireOperand(words, position, functionMode, "Ternary operator : does not have a value when false", colon);
            return new TernaryNode(left, whenTrue, ParseConditional(words, ref position, functionMode));
        }

        return left;
    }

    private SimpleNode ParseLogical(List<(string Text, int Index)> words, ref int position, bool functionMode)
    {
        var left = ParseBinary(words, ref position, functionMode);
        while (position < words.Count && words[position].Text is "&&" or "||")
        {
            var (symbol, index) = words[position];
            position++;
            RequireOperand(words, position, functionMode, $"Logical operator {symbol} does not have a right hand side", index);
            var right = ParseBinary(words, ref position, functionMode);
            left = new LogicalNode(left, symbol == "&&", right);
        }

        return left;
    }

    private SimpleNode ParseBinary(List<(string Text, int Index)> words, ref int position, bool functionMode)
    {
        var left = ParseOperand(words, ref position, functionMode);
        if (position >= words.Count || !BinaryOperators.TryGetValue(words[position].Text, out var op))
        {
            return left;
        }

        var (symbol, index) = words[position];
        position++;
        RequireOperand(words, position, functionMode, $"Binary operator {symbol} does not have a right hand side", index);
        var rightIndex = words[position].Index;
        var right = ParseOperand(words, ref position, functionMode);
        try
        {
            return new BinaryNode(left, op, symbol, right);
        }
        catch (ArgumentException e)
        {
            throw new SimpleParseException(e.Message, _expression, rightIndex, e);
        }
    }

    private void RequireOperand(List<(string Text, int Index)> words, int position, bool functionMode, string reason, int index)
    {
        if (position >= words.Count || IsOperator(words[position].Text, functionMode))
        {
            throw Error(reason, index);
        }
    }

    private SimpleNode ParseOperand(List<(string Text, int Index)> words, ref int position, bool functionMode)
    {
        if (position >= words.Count)
        {
            throw Error("Unexpected end of expression", _expression.Length);
        }

        var (text, index) = words[position];
        if (IsOperator(text, functionMode))
        {
            throw BinaryOperators.ContainsKey(text) || text is "&&" or "||"
                ? Error($"{(BinaryOperators.ContainsKey(text) ? "Binary" : "Logical")} operator {text} does not have a left hand side", index)
                : Error($"Unexpected operator {text}", index);
        }

        position++;
        if (IsQuoted(text))
        {
            return Template(text[1..^1], index + 1, allowIncDec: false);
        }

        switch (text)
        {
            case "null":
                return new LiteralNode(null);
            case "true":
                return new LiteralNode(true);
            case "false":
                return new LiteralNode(false);
        }

        if (ParseNumber(text) is { } number)
        {
            return new LiteralNode(number);
        }

        if (functionMode)
        {
            return FunctionStart(text, 0) > 0 ? Template(text, index, allowIncDec: false) : FunctionText(text, index);
        }

        return HasFunction(text) ? Template(text, index, allowIncDec: false) : new LiteralNode(text);
    }
}
