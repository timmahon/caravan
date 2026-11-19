namespace Caravan;

/// <summary>Well-known header names.</summary>
public static class CaravanHeaders
{
    /// <summary>Overrides the logger category used when logging an exchange.</summary>
    public const string LoggerName = "CaravanLogger";

    /// <summary>The time the message was created, in Unix milliseconds (set by <c>timer</c> and <c>scheduler</c> with <c>includeMetadata</c>).</summary>
    public const string MessageTimestamp = "CaravanMessageTimestamp";

    /// <summary>When the <c>timer</c> or <c>scheduler</c> fired (header name kept from Camel).</summary>
    public const string FiredTime = "firedTime";

    /// <summary>True when the message is being redelivered by an error handler (Camel's <c>Exchange.REDELIVERED</c>).</summary>
    public const string Redelivered = "CaravanRedelivered";

    /// <summary>The number of the current redelivery attempt, starting at 1 (Camel's <c>Exchange.REDELIVERY_COUNTER</c>).</summary>
    public const string RedeliveryCounter = "CaravanRedeliveryCounter";

    /// <summary>The maximum number of redeliveries, when limited (Camel's <c>Exchange.REDELIVERY_MAX_COUNTER</c>).</summary>
    public const string RedeliveryMaxCounter = "CaravanRedeliveryMaxCounter";

    /// <summary>
    /// The id of the saga the exchange belongs to (Camel's <c>Exchange.SAGA_LONG_RUNNING_ACTION</c>, the LRA header). Set by the <c>saga</c> EIP
    /// and sent to compensation and completion endpoints.
    /// </summary>
    public const string SagaLongRunningAction = "Long-Running-Action";

    /// <summary>
    /// The URI of the endpoint intercepted by <c>interceptSendToEndpoint</c>, or of the route's endpoint for <c>interceptFrom</c>
    /// (Camel's <c>Exchange.INTERCEPTED_ENDPOINT</c>; <c>interceptFrom</c> also sets it as an exchange property).
    /// </summary>
    public const string InterceptedEndpoint = "CaravanInterceptedEndpoint";

    /// <summary>The media type of the body (Camel's <c>Exchange.CONTENT_TYPE</c>), set by <c>marshal</c> with data formats such as <c>json</c>.</summary>
    public const string ContentType = "Content-Type";

    /// <summary>The type to unmarshal JSON to, when the <c>json</c> data format allows it (Camel's <c>CamelJacksonUnmarshalType</c>).</summary>
    public const string JsonUnmarshalType = "CaravanJacksonUnmarshalType";

    /// <summary>The index of a message produced by a <c>dataset</c> endpoint (Camel's <c>CamelDataSetIndex</c>).</summary>
    public const string DataSetIndex = "CaravanDataSetIndex";
}
