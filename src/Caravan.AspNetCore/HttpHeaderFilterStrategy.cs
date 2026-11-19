using Caravan.Support;

namespace Caravan.AspNetCore;

/// <summary>
/// The default header filter of <c>platform-http</c> (Camel's <c>HttpHeaderFilterStrategy</c>): <c>Caravan*</c> headers are filtered both
/// ways; request-only and hop-by-hop headers (<c>Host</c>, <c>Content-Length</c>, <c>Transfer-Encoding</c>, <c>Connection</c>, <c>Accept*</c>,
/// <c>Authorization</c>, <c>Cookie</c>, ...) are not copied to responses. Other request headers, query parameters and path parameters that
/// are still on the message are sent back as response headers, as in Camel; remove them in the route when that is not wanted.
/// </summary>
public class HttpHeaderFilterStrategy : DefaultHeaderFilterStrategy
{
    private static readonly string[] OutDefaults =
    [
        "content-length", "content-type", "host", "connection", "keep-alive", "proxy-connection", "transfer-encoding", "te", "trailer",
        "upgrade", "via", "expect", "accept", "accept-charset", "accept-encoding", "accept-language", "authorization", "proxy-authorization",
        "cookie", "user-agent", "origin", "referer", "max-forwards", "range", "if-match", "if-none-match", "if-modified-since",
        "if-unmodified-since", "if-range", "access-control-request-method", "access-control-request-headers",
    ];

    /// <summary>Creates the strategy with the defaults.</summary>
    public HttpHeaderFilterStrategy()
    {
        foreach (var name in OutDefaults)
        {
            OutFilter.Add(name);
        }

        OutFilterStartsWith.Add("Sec-");
        InFilter.Add("content-length");
        InFilter.Add("transfer-encoding");
    }
}
