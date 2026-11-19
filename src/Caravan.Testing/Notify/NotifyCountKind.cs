namespace Caravan.Testing.Notify;

/// <summary>Which exchange events a <see cref="CountNotifyPredicate"/> counts.</summary>
internal enum NotifyCountKind
{
    /// <summary>Exchanges created (received by a route).</summary>
    Received,

    /// <summary>Exchanges done: completed or failed.</summary>
    Done,

    /// <summary>Exchanges completed successfully.</summary>
    Completed,

    /// <summary>Exchanges failed.</summary>
    Failed,
}
