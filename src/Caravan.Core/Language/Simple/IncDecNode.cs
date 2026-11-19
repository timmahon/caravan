using System.Globalization;

namespace Caravan.Language.Simple;

/// <summary>
/// <c>${function}++</c> / <c>${function}--</c>: the function's numeric value plus or minus one, converted back to the value's type. As
/// in Camel the source (body, header or exchange property) is updated with the new value.
/// </summary>
internal sealed class IncDecNode(SimpleNode operand, int delta, string text) : SimpleNode
{
    /// <inheritdoc />
    public override object? Evaluate(Exchange exchange)
    {
        var value = operand.Evaluate(exchange);
        if (value is null)
        {
            return null;
        }

        object result;
        try
        {
            var number = (long)exchange.Context.TypeConverter.Convert(typeof(long), exchange, value)!;
            var next = number + delta;
            result = value is string
                ? next.ToString(CultureInfo.InvariantCulture)
                : exchange.Context.TypeConverter.Convert(value.GetType(), exchange, next) ?? next;
        }
        catch (Exception e) when (e is not ExpressionEvaluationException)
        {
            throw new ExpressionEvaluationException(text, $"Cannot {(delta > 0 ? "increment" : "decrement")} non-numeric value '{value}' of {text}", e);
        }

        operand.Setter?.Invoke(exchange, result);
        return result;
    }
}
