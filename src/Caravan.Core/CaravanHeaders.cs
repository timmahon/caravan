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
}
