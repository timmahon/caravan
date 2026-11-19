using System.Collections;
using System.Globalization;

namespace Caravan.Support;

/// <summary>
/// Value comparison with type coercion, following Camel's <c>ObjectHelper</c>: values of different types are
/// compared after converting one to the other's type with the context's type converters.
/// </summary>
public static class ObjectHelper
{
    /// <summary>Converts <paramref name="value"/> to a string with the context's type converters; null stays null.</summary>
    public static string? ToText(Exchange exchange, object? value) =>
        value is null ? null : value as string ?? (string?)exchange.Context.TypeConverter.Convert(typeof(string), exchange, value);

    /// <summary>
    /// Whether two values are equal: both null, equal as is, or equal after converting the left value to the
    /// right value's type or the right value to the left value's type.
    /// </summary>
    public static bool TypeCoerceEquals(Exchange exchange, object? left, object? right, bool ignoreCase = false)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (ValueEquals(left, right, ignoreCase))
        {
            return true;
        }

        if (left.GetType().IsInstanceOfType(right))
        {
            return false;
        }

        return (TryConvert(exchange, right.GetType(), left, out var leftConverted) && ValueEquals(leftConverted, right, ignoreCase)) ||
               (TryConvert(exchange, left.GetType(), right, out var rightConverted) && ValueEquals(left, rightConverted, ignoreCase));
    }

    /// <summary>
    /// Compares two values: numerically when both convert to numbers, otherwise after coercing to the non-string
    /// side's type (so <c>"10"</c> vs <c>6</c> compares as numbers). Null is less than any value.
    /// </summary>
    /// <exception cref="ArgumentException">The values cannot be compared.</exception>
    public static int TypeCoerceCompare(Exchange exchange, object? left, object? right)
    {
        if (left is null || right is null)
        {
            return left is null ? (right is null ? 0 : -1) : 1;
        }

        if (TryDecimal(left, out var leftNumber) && TryDecimal(right, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        if (TryDouble(left, out var leftDouble) && TryDouble(right, out var rightDouble))
        {
            return leftDouble.CompareTo(rightDouble);
        }

        if (left is string ls && right is string rs)
        {
            return string.CompareOrdinal(ls, rs);
        }

        // Prefer not to coerce to string: "10" < 6 would be true as strings.
        if (right is string && left is not string)
        {
            return -TypeCoerceCompare(exchange, right, left);
        }

        if (right is IComparable rc && TryConvert(exchange, right.GetType(), left, out var l) && l is not null)
        {
            return -rc.CompareTo(l);
        }

        if (left is IComparable lc && TryConvert(exchange, left.GetType(), right, out var r) && r is not null)
        {
            return lc.CompareTo(r);
        }

        throw new ArgumentException($"Cannot compare {left.GetType().Name} '{left}' with {right.GetType().Name} '{right}'");
    }

    /// <summary>
    /// Whether <paramref name="container"/> contains <paramref name="value"/>: for a collection, any element equals it
    /// (<see cref="TypeCoerceEquals"/>); otherwise the string forms are compared with <see cref="string.Contains(string)"/>.
    /// </summary>
    public static bool TypeCoerceContains(Exchange exchange, object? container, object? value, bool ignoreCase = false)
    {
        if (container is null || value is null)
        {
            return false;
        }

        if (container is IEnumerable items and not string)
        {
            foreach (var item in items)
            {
                if (TypeCoerceEquals(exchange, item, value, ignoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        var text = ToText(exchange, container);
        var part = ToText(exchange, value);
        return text is not null && part is not null &&
               text.Contains(part, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool ValueEquals(object? left, object? right, bool ignoreCase) =>
        ignoreCase && left is string ls && right is string rs
            ? string.Equals(ls, rs, StringComparison.OrdinalIgnoreCase)
            : Equals(left, right);

    private static bool TryConvert(Exchange exchange, Type type, object value, out object? result)
    {
        try
        {
            return exchange.Context.TypeConverter.TryConvert(type, exchange, value, out result);
        }
        catch (Exception)
        {
            result = null;
            return false;
        }
    }

    private static bool TryDecimal(object value, out decimal result)
    {
        result = 0;
        try
        {
            switch (value)
            {
                case string s:
                    return decimal.TryParse(s.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out result);
                case byte or sbyte or short or ushort or int or uint or long or ulong or decimal:
                    result = System.Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    return true;
                case float or double:
                    var d = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    if (double.IsFinite(d))
                    {
                        result = (decimal)d;
                        return true;
                    }

                    return false;
                default:
                    return false;
            }
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TryDouble(object value, out double result)
    {
        result = 0;
        switch (value)
        {
            case string s:
                return double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
            case byte or sbyte or short or ushort or int or uint or long or ulong or decimal or float or double:
                result = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            default:
                return false;
        }
    }
}
