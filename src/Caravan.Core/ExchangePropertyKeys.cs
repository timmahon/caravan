namespace Caravan;

/// <summary>Well-known exchange property and header names used by the routing engine.</summary>
public static class ExchangePropertyKeys
{
    public const string CorrelationId = "CaravanCorrelationId";
    public const string ToEndpoint = "CaravanToEndpoint";
    public const string FailureEndpoint = "CaravanFailureEndpoint";
    public const string FailureRouteId = "CaravanFailureRouteId";
    public const string ExceptionCaught = "CaravanExceptionCaught";
    public const string ErrorHandled = "CaravanErrorHandled";
    public const string FilterMatched = "CaravanFilterMatched";
    public const string CreatedTimestamp = "CaravanCreatedTimestamp";
    public const string StepId = "CaravanStepId";
}

/// <summary>Well-known header names.</summary>
public static class CaravanHeaders
{
    public const string LoggerName = "CaravanLogger";
}
