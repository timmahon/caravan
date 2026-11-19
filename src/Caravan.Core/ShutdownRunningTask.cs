namespace Caravan;

/// <summary>How much pending work a consumer completes during a graceful shutdown (Camel's <c>ShutdownRunningTask</c>).</summary>
public enum ShutdownRunningTask
{
    /// <summary>Complete the exchanges being processed only; messages still queued are left (default).</summary>
    CompleteCurrentTaskOnly,

    /// <summary>
    /// Also process the pending messages of consumers that queue them (e.g. drain a <c>seda</c> queue) before stopping.
    /// </summary>
    CompleteAllTasks,
}
