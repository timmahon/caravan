namespace Caravan.Management;

/// <summary>How often an endpoint was used (Camel's runtime endpoint registry hits).</summary>
internal sealed class EndpointUsage
{
    private long _inbound;
    private long _outbound;

    public long Inbound => Interlocked.Read(ref _inbound);

    public long Outbound => Interlocked.Read(ref _outbound);

    public void IncrementInbound() => Interlocked.Increment(ref _inbound);

    public void IncrementOutbound() => Interlocked.Increment(ref _outbound);

    public void Reset()
    {
        Interlocked.Exchange(ref _inbound, 0);
        Interlocked.Exchange(ref _outbound, 0);
    }
}
