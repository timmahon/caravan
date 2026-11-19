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
    /// <summary>When the exchange failed (set by error handlers when redelivery is exhausted).</summary>
    public const string FailureTimestamp = "CaravanFailureTimestamp";
    /// <summary>True when a failure processor (dead letter channel or <c>onException</c> block) processed the failure.</summary>
    public const string FailureHandled = "CaravanFailureHandled";
    /// <summary>The exception caught by an error handler, <c>doCatch</c> or <c>doFinally</c>.</summary>
    public const string ExceptionCaught = "CaravanExceptionCaught";
    /// <summary>Whether an error handler handled the failure.</summary>
    public const string ErrorHandled = "CaravanErrorHandled";
    /// <summary>Set to true on the exchange processed by an <c>onCompletion</c> block.</summary>
    public const string OnCompletion = "CaravanOnCompletion";
    /// <summary>Whether the last <c>filter</c> (or <c>when</c>) predicate matched.</summary>
    public const string FilterMatched = "CaravanFilterMatched";
    /// <summary>When the exchange was created.</summary>
    public const string CreatedTimestamp = "CaravanCreatedTimestamp";
    /// <summary>The id of the <c>step</c> the exchange is currently in.</summary>
    public const string StepId = "CaravanStepId";
    /// <summary>Overrides the endpoint's <see cref="WaitForTaskToComplete"/> for one exchange sent to <c>seda</c>.</summary>
    public const string AsyncWait = "CaravanAsyncWait";
    /// <summary>The name of the timer that fired the exchange.</summary>
    public const string TimerName = "CaravanTimerName";
    /// <summary>When the timer fired the exchange.</summary>
    public const string TimerFiredTime = "CaravanTimerFiredTime";
    /// <summary>The period of the timer that fired the exchange.</summary>
    public const string TimerPeriod = "CaravanTimerPeriod";
    /// <summary>How many times the timer has fired (starting at 1).</summary>
    public const string TimerCounter = "CaravanTimerCounter";
    /// <summary>The index (0-based) of the branch of a <c>multicast</c> or <c>recipientList</c> processing the exchange.</summary>
    public const string MulticastIndex = "CaravanMulticastIndex";
    /// <summary>True on the last branch of a <c>multicast</c> or <c>recipientList</c>.</summary>
    public const string MulticastComplete = "CaravanMulticastComplete";
    /// <summary>The index (0-based) of the split message.</summary>
    public const string SplitIndex = "CaravanSplitIndex";
    /// <summary>
    /// The number of split messages: set on every split message when the splitter is not streaming, and only on the last one
    /// when streaming (the total is unknown until then).
    /// </summary>
    public const string SplitSize = "CaravanSplitSize";
    /// <summary>True on the last split message.</summary>
    public const string SplitComplete = "CaravanSplitComplete";
    /// <summary>The URI of the endpoint a <c>recipientList</c> branch is sent to.</summary>
    public const string RecipientListEndpoint = "CaravanRecipientListEndpoint";
    /// <summary>Holds the list being built by the grouped and string aggregation strategies until the aggregation completes.</summary>
    public const string GroupedExchange = "CaravanGroupedExchange";
    /// <summary>Set to false by a <c>scheduler</c> route to report that the poll found no work (drives idle backoff).</summary>
    public const string SchedulerPolledMessages = "CaravanSchedulerPolledMessages";
}
