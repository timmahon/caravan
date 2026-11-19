using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Caravan.AspNetCore;

/// <summary>
/// Serves <c>platform-http</c> consumers through ASP.NET Core endpoint routing: <see cref="EndpointDataSource"/> holds one endpoint per started
/// consumer (plus CORS preflight endpoints) and is added to the application's routing by <c>app.MapCaravan()</c>. One engine is registered
/// per service provider by <c>AddAspNetCore()</c>; consumers of every context using it share it.
/// </summary>
public sealed partial class PlatformHttpEngine
{
    /// <summary>The route value holding the rest of the path for <c>matchOnUriPrefix</c> consumers.</summary>
    internal const string RemainingPathValue = "caravanRemainingPath";

    private readonly object _lock = new();
    private readonly List<PlatformHttpConsumer> _consumers = [];
    private readonly PlatformHttpEndpointDataSource _dataSource;

    /// <summary>Creates an engine.</summary>
    public PlatformHttpEngine()
    {
        _dataSource = new PlatformHttpEndpointDataSource(BuildEndpoints);
    }

    /// <summary>The endpoints of the started consumers, for <c>IEndpointRouteBuilder.DataSources</c>.</summary>
    public EndpointDataSource EndpointDataSource => _dataSource;

    /// <summary>The conventions applied to the consumers' endpoints (e.g. authorization).</summary>
    public IEndpointConventionBuilder Conventions => _dataSource;

    /// <summary>The started consumers.</summary>
    public IReadOnlyList<PlatformHttpConsumer> Consumers
    {
        get
        {
            lock (_lock)
            {
                return [.. _consumers];
            }
        }
    }

    /// <summary>Adds a started consumer's endpoint.</summary>
    /// <param name="consumer">The consumer.</param>
    /// <exception cref="InvalidOperationException">Another consumer serves the same path and method.</exception>
    internal void Register(PlatformHttpConsumer consumer)
    {
        lock (_lock)
        {
            var key = PatternKey(consumer.PlatformHttpEndpoint);
            var methods = consumer.PlatformHttpEndpoint.Methods;
            foreach (var other in _consumers.Where(c => PatternKey(c.PlatformHttpEndpoint) == key))
            {
                var otherMethods = other.PlatformHttpEndpoint.Methods;
                if (methods.Count == 0 || otherMethods.Count == 0 || methods.Intersect(otherMethods).Any())
                {
                    throw new InvalidOperationException(
                        $"Cannot serve {consumer.Endpoint.EndpointUri}: {other.Endpoint.EndpointUri} already serves the same path and method");
                }
            }

            _consumers.Add(consumer);
        }

        _dataSource.Changed();
    }

    /// <summary>Removes a consumer's endpoint.</summary>
    /// <param name="consumer">The consumer.</param>
    internal void Unregister(PlatformHttpConsumer consumer)
    {
        lock (_lock)
        {
            if (!_consumers.Remove(consumer))
            {
                return;
            }
        }

        _dataSource.Changed();
    }

    private static string PatternKey(PlatformHttpEndpoint endpoint) =>
        ParameterRegex().Replace(endpoint.Path.TrimEnd('/'), "{}").ToLowerInvariant() + (endpoint.MatchOnUriPrefix ? "/**" : string.Empty);

    private static RoutePattern Pattern(PlatformHttpEndpoint endpoint) => RoutePatternFactory.Parse(endpoint.MatchOnUriPrefix
        ? endpoint.Path.TrimEnd('/') + "/{**" + RemainingPathValue + "}"
        : endpoint.Path);

    private IReadOnlyList<Endpoint> BuildEndpoints()
    {
        List<PlatformHttpConsumer> consumers;
        lock (_lock)
        {
            consumers = [.. _consumers];
        }

        var conventions = _dataSource.Conventions;
        var endpoints = new List<Endpoint>();
        foreach (var consumer in consumers)
        {
            var endpoint = consumer.PlatformHttpEndpoint;
            var builder = new RouteEndpointBuilder(context => PlatformHttpBinding.HandleAsync(context, consumer), Pattern(endpoint), 0)
            {
                DisplayName = $"Caravan {endpoint.HttpMethodRestrict ?? "*"} {endpoint.Path} (route {consumer.Endpoint.EndpointUri})",
            };
            if (endpoint.Methods.Count > 0)
            {
                builder.Metadata.Add(new HttpMethodMetadata(endpoint.Methods));
            }

            builder.Metadata.Add(endpoint);
            endpoints.Add(Build(builder, conventions));
        }

        // CORS preflight: answered for paths whose consumers enable CORS and do not serve OPTIONS themselves.
        foreach (var group in consumers.GroupBy(c => PatternKey(c.PlatformHttpEndpoint)))
        {
            var cors = group.FirstOrDefault(c => c.PlatformHttpEndpoint is { EnableCors: true, CorsHeaders: not null });
            if (cors is null || group.Any(c => c.PlatformHttpEndpoint.Methods.Count == 0 || c.PlatformHttpEndpoint.Methods.Contains("OPTIONS")))
            {
                continue;
            }

            var headers = cors.PlatformHttpEndpoint.CorsHeaders!;
            var builder = new RouteEndpointBuilder(context => PlatformHttpBinding.PreflightAsync(context, headers), Pattern(cors.PlatformHttpEndpoint), 0)
            {
                DisplayName = $"Caravan CORS preflight {cors.PlatformHttpEndpoint.Path}",
            };
            builder.Metadata.Add(new HttpMethodMetadata(["OPTIONS"]));
            endpoints.Add(Build(builder, conventions));
        }

        return endpoints;
    }

    private static Endpoint Build(RouteEndpointBuilder builder, IReadOnlyList<Action<EndpointBuilder>> conventions)
    {
        foreach (var convention in conventions)
        {
            convention(builder);
        }

        return builder.Build();
    }

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex ParameterRegex();
}
