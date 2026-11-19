namespace Caravan;

/// <summary>Well-known exchange property and header names used by the routing engine.</summary>
public static class ExchangePropertyKeys
{
    /// <summary>The id of the exchange this exchange was copied from (split, multicast, wire tap).</summary>
    public const string CorrelationId = "CaravanCorrelationId";
    /// <summary>The URI of the last endpoint the exchange was sent to.</summary>
    public const string ToEndpoint = "CaravanToEndpoint";
    /// <summary>The URI of the endpoint that failed.</summary>
    public const string FailureEndpoint = "CaravanFailureEndpoint";
    /// <summary>The id of the route in which the exchange failed.</summary>
    public const string FailureRouteId = "CaravanFailureRouteId";
    /// <summary>The exception caught by an error handler.</summary>
    public const string ExceptionCaught = "CaravanExceptionCaught";
    /// <summary>Whether an error handler handled the failure.</summary>
    public const string ErrorHandled = "CaravanErrorHandled";
    /// <summary>Whether the last <c>filter</c> (or <c>when</c>) predicate matched.</summary>
    public const string FilterMatched = "CaravanFilterMatched";
    /// <summary>When the exchange was created.</summary>
    public const string CreatedTimestamp = "CaravanCreatedTimestamp";
    /// <summary>The id of the <c>step</c> the exchange is currently in.</summary>
    public const string StepId = "CaravanStepId";
}
