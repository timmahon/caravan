using System.Text.RegularExpressions;
using Caravan.Support;

namespace Caravan.Components.Mock;

/// <summary>
/// Expectations on one value of the selected message(s) of a <see cref="MockAssertionClause"/>: the body, a header, a property or an
/// expression. Comparisons coerce types like Camel (<c>"10"</c> equals <c>10</c>). Each method returns the assertion clause, so that more
/// conditions can follow.
/// </summary>
public sealed class MockValueClause
{
    private readonly MockAssertionClause _clause;
    private readonly string _description;
    private readonly Func<Exchange, object?> _value;

    internal MockValueClause(MockAssertionClause clause, string description, Func<Exchange, object?> value)
    {
        _clause = clause;
        _description = description;
        _value = value;
    }

    /// <summary>Expects the value to equal <paramref name="expected"/>.</summary>
    /// <param name="expected">The expected value.</param>
    /// <returns>The assertion clause.</returns>
    public MockAssertionClause IsEqualTo(object? expected) =>
        Check((e, v) => ObjectHelper.TypeCoerceEquals(e, v, expected), $"equal to <{expected}>");

    /// <summary>Expects the value not to equal <paramref name="value"/>.</summary>
    /// <param name="value">The value it must differ from.</param>
    /// <returns>The assertion clause.</returns>
    public MockAssertionClause IsNotEqualTo(object? value) =>
        Check((e, v) => !ObjectHelper.TypeCoerceEquals(e, v, value), $"not equal to <{value}>");

    /// <summary>Expects the value to be null.</summary>
    /// <returns>The assertion clause.</returns>
    public MockAssertionClause IsNull() => Check((_, v) => v is null, "null");

    /// <summary>Expects the value not to be null.</summary>
    /// <returns>The assertion clause.</returns>
    public MockAssertionClause IsNotNull() => Check((_, v) => v is not null, "not null");

    /// <summary>Expects the value to be a <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The expected type.</typeparam>
    /// <returns>The assertion clause.</returns>
    public MockAssertionClause IsInstanceOf<T>() => Check((_, v) => v is T, $"an instance of {typeof(T).Name}");

    /// <summary>Expects the value to be greater than <paramref name="value"/>.</summary>
    /// <param name="value">The bound.</param>
    /// <returns>The assertion clause.</returns>
    public MockAssertionClause IsGreaterThan(object value) =>
        Check((e, v) => v is not null && ObjectHelper.TypeCoerceCompare(e, v, value) > 0, $"greater than <{value}>");

    /// <summary>Expects the value to be greater than or equal to <paramref name="value"/>.</summary>
    /// <param name="value">The bound.</param>
    /// <returns>The assertion clause.</returns>
    public MockAssertionClause IsGreaterThanOrEqualTo(object value) =>
        Check((e, v) => v is not null && ObjectHelper.TypeCoerceCompare(e, v, value) >= 0, $"greater than or equal to <{value}>");

    /// <summary>Expects the value to be less than <paramref name="value"/>.</summary>
    /// <param name="value">The bound.</param>
    /// <returns>The assertion clause.</returns>
    public MockAssertionClause IsLessThan(object value) =>
        Check((e, v) => v is not null && ObjectHelper.TypeCoerceCompare(e, v, value) < 0, $"less than <{value}>");

    /// <summary>Expects the value to be less than or equal to <paramref name="value"/>.</summary>
    /// <param name="value">The bound.</param>
    /// <returns>The assertion clause.</returns>
    public MockAssertionClause IsLessThanOrEqualTo(object value) =>
        Check((e, v) => v is not null && ObjectHelper.TypeCoerceCompare(e, v, value) <= 0, $"less than or equal to <{value}>");

    /// <summary>Expects the value to contain <paramref name="value"/> (an element of a collection, or a substring).</summary>
    /// <param name="value">The contained value.</param>
    /// <returns>The assertion clause.</returns>
    public MockAssertionClause Contains(object? value) =>
        Check((e, v) => ObjectHelper.TypeCoerceContains(e, v, value), $"containing <{value}>");

    /// <summary>Expects the value's text to start with <paramref name="prefix"/>.</summary>
    /// <param name="prefix">The prefix.</param>
    /// <returns>The assertion clause.</returns>
    public MockAssertionClause StartsWith(string prefix) =>
        Check((e, v) => ObjectHelper.ToText(e, v)?.StartsWith(prefix, StringComparison.Ordinal) == true, $"starting with <{prefix}>");

    /// <summary>Expects the value's text to end with <paramref name="suffix"/>.</summary>
    /// <param name="suffix">The suffix.</param>
    /// <returns>The assertion clause.</returns>
    public MockAssertionClause EndsWith(string suffix) =>
        Check((e, v) => ObjectHelper.ToText(e, v)?.EndsWith(suffix, StringComparison.Ordinal) == true, $"ending with <{suffix}>");

    /// <summary>Expects the value's text to match the regular expression <paramref name="pattern"/> (anywhere in the text).</summary>
    /// <param name="pattern">The regular expression.</param>
    /// <returns>The assertion clause.</returns>
    public MockAssertionClause Regex(string pattern) =>
        Check((e, v) => ObjectHelper.ToText(e, v) is { } text && System.Text.RegularExpressions.Regex.IsMatch(text, pattern, RegexOptions.None, TimeSpan.FromSeconds(1)), $"matching /{pattern}/");

    /// <summary>Expects the value to satisfy <paramref name="predicate"/>.</summary>
    /// <param name="predicate">The condition on the value.</param>
    /// <param name="description">Describes the condition in failure messages.</param>
    /// <returns>The assertion clause.</returns>
    public MockAssertionClause Matches(Func<object?, bool> predicate, string description = "the predicate") =>
        Check((_, v) => predicate(v), $"matching {description}");

    /// <summary>Expects the value, converted to <typeparamref name="T"/>, to satisfy <paramref name="predicate"/>.</summary>
    /// <typeparam name="T">The type to convert the value to.</typeparam>
    /// <param name="predicate">The condition on the converted value.</param>
    /// <param name="description">Describes the condition in failure messages.</param>
    /// <returns>The assertion clause.</returns>
    public MockAssertionClause Matches<T>(Func<T?, bool> predicate, string description = "the predicate") =>
        Check((e, v) => predicate((T?)e.Context.TypeConverter.Convert(typeof(T), e, v)), $"matching {description}");

    private MockAssertionClause Check(Func<Exchange, object?, bool> condition, string expectation) =>
        _clause.AddCheck(e =>
        {
            var value = _value(e);
            try
            {
                return condition(e, value) ? null : $"expected {_description} to be {expectation} but was <{value}>";
            }
            catch (Exception ex)
            {
                return $"expected {_description} to be {expectation} but was <{value}> ({ex.GetType().Name}: {ex.Message})";
            }
        });
}
