using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Observability;

/// <summary>What spans and metrics show about an endpoint, computed once per endpoint user (route or producer).</summary>
internal sealed class EndpointTelemetryInfo
{
    public EndpointTelemetryInfo(IEndpoint endpoint)
    {
        var uri = endpoint.EndpointUri;
        SanitizedUri = UriSanitizer.Sanitize(uri) ?? uri;
        Name = UriSanitizer.WithoutQuery(uri);
        try
        {
            var parsed = EndpointUri.Parse(uri);
            Scheme = parsed.Scheme;
            Path = parsed.Path;
        }
        catch (ArgumentException)
        {
            Scheme = string.Empty;
            Path = Name;
        }
    }

    /// <summary>The URI without secrets.</summary>
    public string SanitizedUri { get; }

    /// <summary>The URI without options (low cardinality), e.g. <c>seda://orders</c>.</summary>
    public string Name { get; }

    /// <summary>The component scheme.</summary>
    public string Scheme { get; }

    /// <summary>The path (destination).</summary>
    public string Path { get; }
}
