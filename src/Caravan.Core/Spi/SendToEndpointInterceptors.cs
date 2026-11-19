using Caravan.Impl;

namespace Caravan.Spi;

/// <summary>
/// The <see cref="ISendToEndpointInterceptor"/>s of a context (a context extension, see <see cref="For"/>). Producers created with
/// <see cref="CreateProducer"/> (by <c>to</c> and every EIP sending through a producer cache, and by producer templates) consult it on every
/// send, so interceptors added or removed later apply to existing producers too. The first interceptor added is the outermost.
/// </summary>
public sealed class SendToEndpointInterceptors
{
    private readonly Lock _lock = new();
    private volatile ISendToEndpointInterceptor[] _interceptors = [];
    private int _version;

    /// <summary>The registered interceptors, in order.</summary>
    public IReadOnlyList<ISendToEndpointInterceptor> Interceptors => _interceptors;

    /// <summary>Incremented on every change (lets producers cache which interceptors match their endpoint).</summary>
    internal int Version => Volatile.Read(ref _version);

    /// <summary>The interceptors of <paramref name="context"/>, created on first use.</summary>
    /// <param name="context">The context.</param>
    /// <returns>Its interceptors.</returns>
    public static SendToEndpointInterceptors For(ICaravanContext context) => context.GetOrAddExtension(() => new SendToEndpointInterceptors());

    /// <summary>
    /// Creates a producer for <paramref name="endpoint"/> (<see cref="IEndpoint.CreateProducer"/>) that applies the context's send interceptors.
    /// Code that sends to endpoints should create producers this way so that interception and endpoint mocking see its sends.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <returns>The (not yet started) producer.</returns>
    public static IProducer CreateProducer(IEndpoint endpoint) =>
        new InterceptSendToEndpointProducer(endpoint.CreateProducer(), For(endpoint.Context));

    /// <summary>Adds an interceptor (does nothing when it is already registered).</summary>
    /// <param name="interceptor">The interceptor.</param>
    public void Add(ISendToEndpointInterceptor interceptor)
    {
        ArgumentNullException.ThrowIfNull(interceptor);
        lock (_lock)
        {
            if (_interceptors.Contains(interceptor))
            {
                return;
            }

            _interceptors = [.. _interceptors, interceptor];
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>Adds the interceptor returned by <paramref name="factory"/> unless one matching <paramref name="existing"/> is registered.</summary>
    /// <typeparam name="T">The interceptor type.</typeparam>
    /// <param name="existing">Finds an equivalent registered interceptor.</param>
    /// <param name="factory">Creates the interceptor.</param>
    /// <returns>The registered interceptor (existing or new).</returns>
    public T GetOrAdd<T>(Func<T, bool> existing, Func<T> factory)
        where T : class, ISendToEndpointInterceptor
    {
        lock (_lock)
        {
            if (_interceptors.OfType<T>().FirstOrDefault(existing) is { } found)
            {
                return found;
            }

            var created = factory();
            _interceptors = [.. _interceptors, created];
            Interlocked.Increment(ref _version);
            return created;
        }
    }

    /// <summary>Removes an interceptor; returns whether it was registered.</summary>
    /// <param name="interceptor">The interceptor.</param>
    /// <returns>Whether it was removed.</returns>
    public bool Remove(ISendToEndpointInterceptor interceptor)
    {
        lock (_lock)
        {
            if (!_interceptors.Contains(interceptor))
            {
                return false;
            }

            _interceptors = [.. _interceptors.Where(i => !ReferenceEquals(i, interceptor))];
            Interlocked.Increment(ref _version);
            return true;
        }
    }

    /// <summary>The current interceptors with the version they belong to.</summary>
    /// <remarks>Lock-free: the version is read first, so a snapshot is never older than its version.</remarks>
    internal (ISendToEndpointInterceptor[] Interceptors, int Version) Snapshot()
    {
        var version = Volatile.Read(ref _version);
        return (_interceptors, version);
    }
}
