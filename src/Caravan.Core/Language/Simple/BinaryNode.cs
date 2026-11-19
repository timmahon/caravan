using System.Collections;
using System.Text.RegularExpressions;
using Caravan.Support;

namespace Caravan.Language.Simple;

/// <summary>A binary operator applied to two operands, with Camel's type coercion (<see cref="ObjectHelper"/>).</summary>
internal sealed class BinaryNode : SimpleNode
{
    private readonly SimpleNode _left;
    private readonly BinaryOperator _operator;
    private readonly SimpleNode _right;
    private readonly string _symbol;
    private readonly Regex? _regex;
    private readonly (object? From, object? To)? _range;
    private readonly Lazy<Type?>? _type;

    /// <summary>Creates the node; constant right operands of <c>regex</c>, <c>range</c> and <c>is</c> are prepared (and validated) here.</summary>
    /// <exception cref="ArgumentException">A constant right operand is invalid for the operator.</exception>
    public BinaryNode(SimpleNode left, BinaryOperator op, string symbol, SimpleNode right)
    {
        _left = left;
        _operator = op;
        _right = right;
        _symbol = symbol;
        if (right is LiteralNode { Value: var constant })
        {
            switch (op)
            {
                case BinaryOperator.Regex or BinaryOperator.NotRegex:
                    _regex = CreateRegex(constant?.ToString() ?? throw new ArgumentException("regex operator requires a pattern"));
                    break;
                case BinaryOperator.Range or BinaryOperator.NotRange:
                    _range = ParseRange(constant?.ToString());
                    break;
                case BinaryOperator.Is or BinaryOperator.NotIs:
                    var name = constant?.ToString() ?? throw new ArgumentException("is operator requires a type name");
                    _type = new Lazy<Type?>(() => TypeNameResolver.Resolve(name));
                    break;
            }
        }
    }

    /// <inheritdoc />
    public override object? Evaluate(Exchange exchange) => Matches(exchange);

    /// <inheritdoc />
    public override bool Matches(Exchange exchange)
    {
        var left = _left.Evaluate(exchange);
        var right = _right.Evaluate(exchange);
        try
        {
            return _operator switch
            {
                BinaryOperator.Equal => ObjectHelper.TypeCoerceEquals(exchange, left, right),
                BinaryOperator.EqualIgnoreCase => ObjectHelper.TypeCoerceEquals(exchange, left, right, ignoreCase: true),
                BinaryOperator.NotEqual => !ObjectHelper.TypeCoerceEquals(exchange, left, right),
                BinaryOperator.NotEqualIgnoreCase => !ObjectHelper.TypeCoerceEquals(exchange, left, right, ignoreCase: true),
                BinaryOperator.GreaterThan => Compare(exchange, left, right, bothNull: false, c => c > 0),
                BinaryOperator.GreaterThanOrEqual => Compare(exchange, left, right, bothNull: true, c => c >= 0),
                BinaryOperator.LessThan => Compare(exchange, left, right, bothNull: false, c => c < 0),
                BinaryOperator.LessThanOrEqual => Compare(exchange, left, right, bothNull: true, c => c <= 0),
                BinaryOperator.Contains => ObjectHelper.TypeCoerceContains(exchange, left, right),
                BinaryOperator.NotContains => !ObjectHelper.TypeCoerceContains(exchange, left, right),
                BinaryOperator.ContainsIgnoreCase => ObjectHelper.TypeCoerceContains(exchange, left, right, ignoreCase: true),
                BinaryOperator.NotContainsIgnoreCase => !ObjectHelper.TypeCoerceContains(exchange, left, right, ignoreCase: true),
                BinaryOperator.Regex => MatchesRegex(exchange, left, right),
                BinaryOperator.NotRegex => !MatchesRegex(exchange, left, right),
                BinaryOperator.In => In(exchange, left, right),
                BinaryOperator.NotIn => !In(exchange, left, right),
                BinaryOperator.Range => InRange(exchange, left, right),
                BinaryOperator.NotRange => !InRange(exchange, left, right),
                BinaryOperator.Is => IsInstance(left, right),
                BinaryOperator.NotIs => !IsInstance(left, right),
                BinaryOperator.StartsWith => Text(exchange, left, right, (l, r) => l.StartsWith(r, StringComparison.Ordinal)),
                BinaryOperator.NotStartsWith => !Text(exchange, left, right, (l, r) => l.StartsWith(r, StringComparison.Ordinal)),
                BinaryOperator.EndsWith => Text(exchange, left, right, (l, r) => l.EndsWith(r, StringComparison.Ordinal)),
                BinaryOperator.NotEndsWith => !Text(exchange, left, right, (l, r) => l.EndsWith(r, StringComparison.Ordinal)),
                _ => throw new InvalidOperationException(_operator.ToString()),
            };
        }
        catch (ArgumentException e)
        {
            throw new ExpressionEvaluationException(ToString(), $"Cannot evaluate {this}: {e.Message}", e);
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"{_left} {_symbol} {_right}";

    private static bool Compare(Exchange exchange, object? left, object? right, bool bothNull, Func<int, bool> test)
    {
        if (left is null || right is null)
        {
            // Camel: comparing with null is false, except null >= null and null <= null.
            return left is null && right is null && bothNull;
        }

        return test(ObjectHelper.TypeCoerceCompare(exchange, left, right));
    }

    private bool MatchesRegex(Exchange exchange, object? left, object? right)
    {
        var regex = _regex ?? (ObjectHelper.ToText(exchange, right) is { } pattern ? CreateRegex(pattern) : null);
        return regex is not null && ObjectHelper.ToText(exchange, left) is { } text && regex.IsMatch(text);
    }

    private static Regex CreateRegex(string pattern) =>
        new(@"\A(?:" + pattern + @")\z", RegexOptions.CultureInvariant);

    private static bool In(Exchange exchange, object? left, object? right)
    {
        IEnumerable values = right switch
        {
            null => Array.Empty<object>(),
            string s => s.Split(',').Select(v => v.Trim()),
            IEnumerable e => e,
            var single => new[] { single },
        };
        foreach (var value in values)
        {
            if (ObjectHelper.TypeCoerceEquals(exchange, left, value))
            {
                return true;
            }
        }

        return false;
    }

    private bool InRange(Exchange exchange, object? left, object? right)
    {
        var (from, to) = _range ?? ParseRange(ObjectHelper.ToText(exchange, right));
        return left is not null &&
               ObjectHelper.TypeCoerceCompare(exchange, left, from) >= 0 &&
               ObjectHelper.TypeCoerceCompare(exchange, left, to) <= 0;
    }

    private static (object? From, object? To) ParseRange(string? range)
    {
        var dots = range?.IndexOf("..", StringComparison.Ordinal) ?? -1;
        if (range is null || dots <= 0 || dots + 2 >= range.Length)
        {
            throw new ArgumentException($"Syntax error in range operator: '{range}' is not of the form from..to");
        }

        return (SimpleParser.ParseNumber(range[..dots].Trim()) ?? range[..dots].Trim(), SimpleParser.ParseNumber(range[(dots + 2)..].Trim()) ?? range[(dots + 2)..].Trim());
    }

    private bool IsInstance(object? left, object? right)
    {
        var type = _type is not null ? _type.Value : right as Type ?? (right is null ? null : TypeNameResolver.Resolve(right.ToString()!));
        if (type is null)
        {
            throw new ArgumentException($"Cannot resolve type '{right}'");
        }

        return type.IsInstanceOfType(left);
    }

    private static bool Text(Exchange exchange, object? left, object? right, Func<string, string, bool> test) =>
        ObjectHelper.ToText(exchange, left) is { } l && ObjectHelper.ToText(exchange, right) is { } r && test(l, r);
}
