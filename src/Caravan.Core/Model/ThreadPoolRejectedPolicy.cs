namespace Caravan.Model;

/// <summary>What the <c>threads</c> EIP does with an exchange when its pool and queue are full (Camel 4's <c>ThreadPoolRejectedPolicy</c>).</summary>
public enum ThreadPoolRejectedPolicy
{
    /// <summary>Fails the exchange with a <see cref="RejectedExecutionException"/>.</summary>
    Abort,

    /// <summary>Processes the exchange on the caller's flow (no hand-off), which slows the caller down (back-pressure).</summary>
    CallerRuns,
}
