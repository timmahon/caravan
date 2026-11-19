using System.Collections.Concurrent;

namespace Caravan.Components.Seda;

/// <summary>
/// The <c>vm</c> component: <c>seda</c> queues shared by all contexts in the process (Camel 3 merged <c>vm</c> into
/// <c>seda</c>; the scheme is kept for compatibility). A consumer receives a copy of the exchange bound to its own
/// context. Queues live for the lifetime of the process.
/// <para>URI: <c>vm:name[?options]</c></para>
/// </summary>
public sealed class VmComponent : SedaComponent
{
    private static readonly ConcurrentDictionary<string, SedaQueue> SharedQueues = new(StringComparer.Ordinal);

    /// <inheritdoc />
    protected override string Scheme => "vm";

    internal override ConcurrentDictionary<string, SedaQueue> Queues => SharedQueues;
}
