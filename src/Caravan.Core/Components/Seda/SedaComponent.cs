using System.Collections.Concurrent;
using Caravan.Support;

namespace Caravan.Components.Seda;

/// <summary>
/// The <c>seda</c> component: asynchronous, in-memory queues between routes of a context, built on
/// <see cref="System.Threading.Channels"/>. Endpoints with the same name share one queue; its <c>size</c> is fixed
/// by the first endpoint, and an endpoint giving another size fails, as in Camel.
/// <para>URI: <c>seda:name[?options]</c> (see <see cref="SedaEndpoint"/> for the options)</para>
/// </summary>
public class SedaComponent : ComponentBase
{
    private readonly ConcurrentDictionary<string, SedaQueue> _queues = new(StringComparer.Ordinal);

    /// <summary>Default queue capacity for new queues (default 1000); zero or negative for unbounded.</summary>
    public int QueueSize { get; set; } = 1000;

    /// <summary>Default for the endpoints' <c>concurrentConsumers</c> option (default 1).</summary>
    public int ConcurrentConsumers { get; set; } = 1;

    /// <summary>Default for the endpoints' <c>blockWhenFull</c> option.</summary>
    public bool DefaultBlockWhenFull { get; set; }

    /// <summary>Default for the endpoints' <c>discardWhenFull</c> option.</summary>
    public bool DefaultDiscardWhenFull { get; set; }

    /// <summary>Default for the endpoints' <c>offerTimeout</c> option (zero = wait indefinitely).</summary>
    public TimeSpan DefaultOfferTimeout { get; set; }

    /// <summary>The URI scheme, used in queue keys and messages.</summary>
    protected virtual string Scheme => "seda";

    /// <summary>Whether endpoints ignore unknown URI options (<c>stub</c>).</summary>
    internal virtual bool LenientEndpoints => false;

    /// <summary>The queues by name; <c>vm</c> overrides it with a process-wide registry.</summary>
    internal virtual ConcurrentDictionary<string, SedaQueue> Queues => _queues;

    /// <inheritdoc />
    protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters)
    {
        if (string.IsNullOrEmpty(remaining))
        {
            throw new ArgumentException($"{Scheme} endpoint requires a name, e.g. {Scheme}:orders");
        }

        int? size = null;
        if (parameters.Remove("size", out var rawSize))
        {
            size = (int)RequiredContext.TypeConverter.Convert(typeof(int), null, RequiredContext.ResolvePropertyPlaceholders(rawSize))!;
        }

        var queue = GetOrCreateQueue(remaining, size);
        return new SedaEndpoint(uri, this, remaining, queue)
        {
            ConcurrentConsumers = ConcurrentConsumers,
            BlockWhenFull = DefaultBlockWhenFull,
            DiscardWhenFull = DefaultDiscardWhenFull,
            OfferTimeout = DefaultOfferTimeout,
            Lenient = LenientEndpoints,
        };
    }

    private SedaQueue GetOrCreateQueue(string name, int? size)
    {
        var key = $"{Scheme}://{name}";
        var queue = Queues.GetOrAdd(name, _ => new SedaQueue(key, size ?? QueueSize));
        if (size is { } requested && requested != queue.Size)
        {
            throw new ArgumentException(
                $"Cannot use existing queue {key} as the existing queue size {queue.Size} does not match given queue size {requested}");
        }

        return queue;
    }
}
