namespace Caravan;

/// <summary>Whether an asynchronous hand-off (e.g. sending to <c>seda</c>) waits for the task to complete.</summary>
public enum WaitForTaskToComplete
{
    /// <summary>Never wait: the caller continues immediately.</summary>
    Never,

    /// <summary>Wait only when a reply is expected (<c>InOut</c>); the default.</summary>
    IfReplyExpected,

    /// <summary>Always wait for the task to complete.</summary>
    Always,
}
