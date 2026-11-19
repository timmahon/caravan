namespace Caravan.Model;

/// <summary>
/// Camel's <c>DeadLetterChannel</c>: when redelivery is exhausted the exchange is sent to <see cref="DeadLetterUri"/> and marked
/// handled: the exception moves to <see cref="ExchangePropertyKeys.ExceptionCaught"/>, <see cref="Exchange.Exception"/> is cleared
/// and the caller does not see the failure. <see cref="ExchangePropertyKeys.FailureEndpoint"/>,
/// <see cref="ExchangePropertyKeys.FailureRouteId"/> and <see cref="ExchangePropertyKeys.FailureTimestamp"/> are set.
/// </summary>
public sealed class DeadLetterChannelDefinition : RedeliveryErrorHandlerDefinition
{
    /// <summary>Creates a dead letter channel sending to <paramref name="deadLetterUri"/>.</summary>
    public DeadLetterChannelDefinition(string deadLetterUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deadLetterUri);
        Uri = deadLetterUri;
    }

    /// <summary>The dead letter endpoint URI.</summary>
    public string Uri { get; set; }

    /// <summary>
    /// Whether a new exception thrown while sending to the dead letter endpoint is handled as well (default true, as in Camel,
    /// so a broken dead letter endpoint does not cause endless poison messages). When false the new exception fails the exchange.
    /// </summary>
    public bool HandleNewException { get; set; } = true;

    /// <inheritdoc />
    public override string? DeadLetterUri => Uri;

    /// <inheritdoc />
    public override bool DeadLetterHandleNewException => HandleNewException;

    /// <inheritdoc />
    public override string Name => "DeadLetterChannel";

    /// <inheritdoc />
    public override string ToString() => $"DeadLetterChannel[{Uri}]";
}
