using System.Collections.Concurrent;
using Caravan.Components.Mock;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Testing;

/// <summary>
/// Sends exchanges bound for the endpoints matching its patterns to <c>mock:&lt;uri&gt;</c> first, then (unless skipping) to the endpoint
/// (Camel's <c>InterceptSendToMockEndpointStrategy</c>). <c>mock:</c> endpoints are never mocked.
/// </summary>
internal sealed class MockEndpointsInterceptor : ISendToEndpointInterceptor
{
    private readonly ICaravanContext _context;
    private readonly string[] _patterns;
    private readonly ConcurrentDictionary<string, IProducer> _producers = new(StringComparer.Ordinal);

    private MockEndpointsInterceptor(ICaravanContext context, string[] patterns, bool skip)
    {
        _context = context;
        _patterns = patterns;
        Skip = skip;
    }

    /// <summary>Whether the original endpoint is skipped.</summary>
    public bool Skip { get; }

    /// <summary>Registers (once per set of patterns and skip mode) an interceptor mocking the endpoints matching <paramref name="patterns"/>.</summary>
    public static void Register(ICaravanContext context, IEnumerable<string>? patterns, bool skip)
    {
        string[] normalized = [.. (patterns ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        if (normalized.Length == 0 || normalized.Contains("*"))
        {
            normalized = [];
        }

        SendToEndpointInterceptors.For(context).GetOrAdd(
            existing => existing.Skip == skip && existing._patterns.SequenceEqual(normalized),
            () => new MockEndpointsInterceptor(context, normalized, skip));
    }

    public bool Matches(IEndpoint endpoint)
    {
        var uri = endpoint.EndpointUri;
        if (uri.StartsWith("mock:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return _patterns.Length == 0 || _patterns.Any(p => EndpointHelper.MatchEndpoint(_context, uri, p));
    }

    public async ValueTask SendAsync(Exchange exchange, IEndpoint endpoint, IProcessor next)
    {
        exchange.Message.SetHeader(CaravanHeaders.InterceptedEndpoint, endpoint.EndpointUri);
        var mockUri = EndpointHelper.MockEndpointUri(endpoint.EndpointUri);
        var producer = _producers.GetOrAdd(mockUri, uri => _context.GetEndpoint<MockEndpoint>(uri).CreateProducer());
        await producer.ProcessAsync(exchange).ConfigureAwait(false);
        if (!Skip && !exchange.IsFailed)
        {
            await next.ProcessAsync(exchange).ConfigureAwait(false);
        }
    }

    public override string ToString() => $"MockEndpoints{(Skip ? "AndSkip" : string.Empty)}[{(_patterns.Length == 0 ? "*" : string.Join(", ", _patterns))}]";
}
