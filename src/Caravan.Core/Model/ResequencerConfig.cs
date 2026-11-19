namespace Caravan.Model;

/// <summary>
/// How the <c>resequence</c> EIP reorders exchanges: <see cref="BatchResequencerConfig"/> (collect a batch, sort it, emit it) or
/// <see cref="StreamResequencerConfig"/> (emit exchanges as soon as they are in sequence).
/// </summary>
public abstract class ResequencerConfig
{
    /// <summary>Ignores exchanges whose sequence expression cannot be evaluated (or is null) instead of failing them.</summary>
    public bool IgnoreInvalidExchanges { get; set; }
}
