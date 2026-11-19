namespace Caravan;

/// <summary>Thrown when an expression fails to evaluate on an exchange (Camel's <c>ExpressionEvaluationException</c>).</summary>
public class ExpressionEvaluationException : CaravanException
{
    /// <summary>Creates an exception for <paramref name="expression"/>.</summary>
    public ExpressionEvaluationException(string expression, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Expression = expression;
    }

    /// <summary>The text of the expression that failed.</summary>
    public string Expression { get; }
}
