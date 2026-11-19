namespace Caravan;

/// <summary>Thrown by the <c>idempotentConsumer</c> EIP when the message id expression returns null (Camel's <c>NoMessageIdException</c>).</summary>
public class NoMessageIdException : CaravanExchangeException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="exchange">The exchange without a message id.</param>
    /// <param name="expression">The message id expression.</param>
    public NoMessageIdException(Exchange exchange, IExpression expression)
        : base($"No message ID could be found using expression: {expression} on message exchange", exchange)
    {
        Expression = expression;
    }

    /// <summary>The message id expression.</summary>
    public IExpression Expression { get; }
}
