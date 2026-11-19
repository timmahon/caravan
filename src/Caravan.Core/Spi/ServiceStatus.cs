namespace Caravan.Spi;

/// <summary>Lifecycle state of a <see cref="IService"/>.</summary>
public enum ServiceStatus
{
    /// <summary>Created, never started.</summary>
    Initialized,

    /// <summary>Starting.</summary>
    Starting,

    /// <summary>Started.</summary>
    Started,

    /// <summary>Suspending.</summary>
    Suspending,

    /// <summary>Suspended; can be resumed.</summary>
    Suspended,

    /// <summary>Stopping.</summary>
    Stopping,

    /// <summary>Stopped.</summary>
    Stopped,

    /// <summary>Failed to start.</summary>
    Failed,
}
