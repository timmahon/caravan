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
    /// <summary>
    /// The message history of the exchange (a <c>List&lt;Observability.MessageHistoryEntry&gt;</c>) when <see cref="ICaravanContext.MessageHistory"/> is
    /// enabled.
    /// </summary>
    public const string MessageHistory = "CaravanMessageHistory";
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
    /// <summary>The number of exchanges aggregated into an <c>aggregate</c> group (set on the aggregated exchange).</summary>
    public const string AggregatedSize = "CaravanAggregatedSize";
    /// <summary>
    /// What completed an <c>aggregate</c> group: <c>size</c>, <c>timeout</c>, <c>interval</c>, <c>predicate</c>, <c>consumer</c>,
    /// <c>force</c> or <c>strategy</c> (see <see cref="Aggregation.AggregationCompletedBy"/>).
    /// </summary>
    public const string AggregatedCompletedBy = "CaravanAggregatedCompletedBy";
    /// <summary>The correlation key of a completed <c>aggregate</c> group.</summary>
    public const string AggregatedCorrelationKey = "CaravanAggregatedCorrelationKey";
    /// <summary>The completion timeout (a <see cref="TimeSpan"/>) of an <c>aggregate</c> group completed by timeout.</summary>
    public const string AggregatedTimeout = "CaravanAggregatedTimeout";
    /// <summary>
    /// Set to true by an aggregation strategy on the exchange it returns to complete the current <c>aggregate</c> group
    /// (completed by <c>strategy</c>).
    /// </summary>
    public const string AggregationCompleteCurrentGroup = "CaravanAggregationCompleteCurrentGroup";
    /// <summary>
    /// As a header on an exchange sent to an <c>aggregate</c>: completes all groups, and the exchange itself is not aggregated. As a
    /// property set by an aggregation strategy on the exchange it returns: completes all groups, the current one included.
    /// </summary>
    public const string AggregationCompleteAllGroups = "CaravanAggregationCompleteAllGroups";
    /// <summary>A header on an exchange sent to an <c>aggregate</c>: the exchange is aggregated, then all groups are completed.</summary>
    public const string AggregationCompleteAllGroupsInclusive = "CaravanAggregationCompleteAllGroupsInclusive";
    /// <summary>
    /// The version of an exchange read from an <see cref="Spi.IOptimisticLockingAggregationRepository"/> that keeps versions on the
    /// exchange (such as <see cref="Aggregation.MemoryAggregationRepository"/> with optimistic locking).
    /// </summary>
    public const string AggregationRepositoryVersion = "CaravanAggregationRepositoryVersion";
    /// <summary>The number of exchanges in the batch a batch consumer polled (drives <c>completionFromBatchConsumer</c>).</summary>
    public const string BatchSize = "CaravanBatchSize";
    /// <summary>The index (0-based) of the exchange in the batch a batch consumer polled.</summary>
    public const string BatchIndex = "CaravanBatchIndex";
    /// <summary>True on the last exchange of the batch a batch consumer polled.</summary>
    public const string BatchComplete = "CaravanBatchComplete";
    /// <summary>The URI of the endpoint a <c>routingSlip</c> or <c>dynamicRouter</c> is currently sending to (Camel's <c>CamelSlipEndpoint</c>).</summary>
    public const string SlipEndpoint = "CaravanSlipEndpoint";
    /// <summary>
    /// The number of times (0-based) a <c>dynamicRouter</c> has evaluated its expression for the exchange, set before each evaluation
    /// (no Camel equivalent: it saves keeping a counter in the expression).
    /// </summary>
    public const string DynamicRouterIndex = "CaravanDynamicRouterIndex";
    /// <summary>The index (0-based) of the current <c>loop</c> iteration.</summary>
    public const string LoopIndex = "CaravanLoopIndex";
    /// <summary>The number of iterations of a <c>loop</c> with a count (not set for <c>loopWhile</c>).</summary>
    public const string LoopSize = "CaravanLoopSize";
    /// <summary>Set to true by the <c>idempotentConsumer</c> on a duplicate exchange.</summary>
    public const string DuplicateMessage = "CaravanDuplicateMessage";
    /// <summary>The <see cref="Spi.IClaimCheckRepository"/> of the exchange, created by the first <c>claimCheck</c>.</summary>
    public const string ClaimCheckRepository = "CaravanClaimCheckRepository";
    /// <summary>True when the <c>circuitBreaker</c> block ran and succeeded (Camel's <c>CamelResponseSuccessfulExecution</c>).</summary>
    public const string ResponseSuccessfulExecution = "CaravanResponseSuccessfulExecution";
    /// <summary>True when the response of a <c>circuitBreaker</c> came from its fallback (Camel's <c>CamelResponseFromFallback</c>).</summary>
    public const string ResponseFromFallback = "CaravanResponseFromFallback";
    /// <summary>True when a <c>circuitBreaker</c> did not run its block because the circuit was open (Camel's <c>CamelResponseShortCircuited</c>).</summary>
    public const string ResponseShortCircuited = "CaravanResponseShortCircuited";
    /// <summary>True when the block of a <c>circuitBreaker</c> timed out (Camel's <c>CamelResponseTimedOut</c>).</summary>
    public const string ResponseTimedOut = "CaravanResponseTimedOut";
    /// <summary>
    /// True when a <c>circuitBreaker</c> rejected the call: the circuit was open or the bulkhead was full (Camel's <c>CamelResponseRejected</c>).
    /// </summary>
    public const string ResponseRejected = "CaravanResponseRejected";
    /// <summary>The state of the circuit (a <see cref="Caravan.CircuitBreakerState"/> name) after a <c>circuitBreaker</c> call (Camel's <c>CamelCircuitBreakerState</c>).</summary>
    public const string CircuitBreakerState = "CaravanCircuitBreakerState";

    /// <summary>The id of the route whose node an <c>intercept</c> block is running before (Camel's <c>CamelInterceptedRouteId</c>).</summary>
    public const string InterceptedRouteId = "CaravanInterceptedRouteId";

    /// <summary>The id of the node an <c>intercept</c> block is running before (Camel's <c>CamelInterceptedNodeId</c>).</summary>
    public const string InterceptedNodeId = "CaravanInterceptedNodeId";

    /// <summary>The endpoint URI of the route whose node an <c>intercept</c> block is running before (Camel's <c>CamelInterceptedRouteEndpointUri</c>).</summary>
    public const string InterceptedRouteEndpointUri = "CaravanInterceptedRouteEndpointUri";

    /// <summary>The URI of the route's endpoint, set by <c>interceptFrom</c> (as the header of the same name).</summary>
    public const string InterceptedEndpoint = CaravanHeaders.InterceptedEndpoint;

    /// <summary>Internal: the <c>interceptSendToEndpoint</c> interceptors whose block the exchange is running (they do not intercept it again).</summary>
    internal const string InterceptSendToEndpointActive = "CaravanInterceptSendToEndpointActive";
}
