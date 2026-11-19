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
}
