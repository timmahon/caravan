namespace Caravan;

/// <summary>Message exchange pattern.</summary>
public enum ExchangePattern
{
    /// <summary>Fire and forget (event message).</summary>
    InOnly,

    /// <summary>Request/reply.</summary>
    InOut,
}
