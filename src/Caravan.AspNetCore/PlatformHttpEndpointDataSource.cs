using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;

namespace Caravan.AspNetCore;

/// <summary>
/// The ASP.NET Core endpoints of the started <c>platform-http</c> consumers. Every change replaces the list and fires the change token,
/// so ASP.NET Core's routing rebuilds its matcher at once.
/// </summary>
internal sealed class PlatformHttpEndpointDataSource : EndpointDataSource, IEndpointConventionBuilder
{
    private readonly object _lock = new();
    private readonly List<Action<EndpointBuilder>> _conventions = [];
    private readonly List<Action<EndpointBuilder>> _finallyConventions = [];
    private readonly Func<IReadOnlyList<Endpoint>> _build;
    private IReadOnlyList<Endpoint>? _endpoints;
    private CancellationTokenSource _changes = new();
    private IChangeToken _token;

    public PlatformHttpEndpointDataSource(Func<IReadOnlyList<Endpoint>> build)
    {
        _build = build;
        _token = new CancellationChangeToken(_changes.Token);
    }

    public override IReadOnlyList<Endpoint> Endpoints
    {
        get
        {
            lock (_lock)
            {
                return _endpoints ??= _build();
            }
        }
    }

    /// <summary>The conventions applied to every endpoint (e.g. <c>RequireAuthorization()</c> on <c>MapCaravan()</c>).</summary>
    public IReadOnlyList<Action<EndpointBuilder>> Conventions
    {
        get
        {
            lock (_lock)
            {
                return [.. _conventions, .. _finallyConventions];
            }
        }
    }

    public override IChangeToken GetChangeToken()
    {
        lock (_lock)
        {
            return _token;
        }
    }

    public void Add(Action<EndpointBuilder> convention)
    {
        lock (_lock)
        {
            _conventions.Add(convention);
        }

        Changed();
    }

    public void Finally(Action<EndpointBuilder> finallyConvention)
    {
        lock (_lock)
        {
            _finallyConventions.Add(finallyConvention);
        }

        Changed();
    }

    /// <summary>Rebuilds the endpoints and notifies routing.</summary>
    public void Changed()
    {
        CancellationTokenSource previous;
        lock (_lock)
        {
            _endpoints = null;
            previous = _changes;
            _changes = new CancellationTokenSource();
            _token = new CancellationChangeToken(_changes.Token);
        }

        // Not disposed: a token handed out earlier may still be registered with.
        previous.Cancel();
    }
}
