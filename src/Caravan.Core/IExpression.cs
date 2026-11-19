namespace Caravan;

/// <summary>Evaluates a value from an exchange (Camel's <c>Expression</c>).</summary>
public interface IExpression
{
    /// <summary>Evaluates the expression against <paramref name="exchange"/>.</summary>
    object? Evaluate(Exchange exchange);
}
