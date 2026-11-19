using Caravan.Spi;
using Caravan.Support;

namespace Caravan.AspNetCore;

/// <summary>
/// A <c>platform-http</c> endpoint: a path (an ASP.NET Core route pattern: <c>{name}</c> parameters become headers) and the options below.
/// Exchanges are <see cref="ExchangePattern.InOut"/>: the message at the end of the route is the response. There is no producer.
/// </summary>
public sealed class PlatformHttpEndpoint : EndpointBase
{
    internal PlatformHttpEndpoint(string uri, PlatformHttpComponent component, string path)
        : base(uri, component, component.Context!)
    {
        Path = path;
        ExchangePattern = ExchangePattern.InOut;
    }

    /// <summary>The path served, starting with a slash.</summary>
    public string Path { get; }

    /// <summary>The HTTP methods served, comma-separated (default: all).</summary>
    public string? HttpMethodRestrict { get; set; }

    /// <summary>The media types accepted (comma-separated); a request with another <c>Content-Type</c> gets 415.</summary>
    public string? Consumes { get; set; }

    /// <summary>The media types produced (comma-separated); a request accepting none of them gets 406. The first is the default <c>Content-Type</c>.</summary>
    public string? Produces { get; set; }

    /// <summary>Whether paths below <see cref="Path"/> are served too (the rest of the path is in <see cref="CaravanHeaders.HttpPath"/>).</summary>
    public bool MatchOnUriPrefix { get; set; }

    /// <summary>Whether the response to a failed exchange has an empty body instead of the exception's text.</summary>
    public bool MuteException { get; set; }

    /// <summary>Which headers are copied between requests, messages and responses.</summary>
    public IHeaderFilterStrategy HeaderFilterStrategy { get; set; } = new HttpHeaderFilterStrategy();

    /// <summary>Whether CORS preflight (<c>OPTIONS</c>) requests are answered for the path (set by the REST DSL).</summary>
    public bool EnableCors { get; set; }

    /// <summary>The CORS headers of preflight responses, when <see cref="EnableCors"/>.</summary>
    public IReadOnlyDictionary<string, string>? CorsHeaders { get; set; }

    /// <summary>The methods served, in upper case; empty for all.</summary>
    public IReadOnlyList<string> Methods => string.IsNullOrWhiteSpace(HttpMethodRestrict)
        ? []
        : [.. HttpMethodRestrict.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(m => m.ToUpperInvariant()).Distinct()];

    /// <summary>The component.</summary>
    internal PlatformHttpComponent PlatformHttpComponent => (PlatformHttpComponent)Component!;

    /// <summary>The media types a REST service produces (for the default <c>Content-Type</c> only: the REST binding validates).</summary>
    internal string? RestProduces { get; set; }

    /// <inheritdoc />
    public override IProducer CreateProducer() =>
        throw new NotSupportedException($"Cannot send to {EndpointUri}: platform-http only consumes; use an HTTP client component to call HTTP services");

    /// <inheritdoc />
    public override IConsumer CreateConsumer(IProcessor processor) => new PlatformHttpConsumer(this, processor);
}
