using Caravan.Support;

namespace Caravan.Components.Rest;

/// <summary>
/// The <c>rest</c> component (Camel's <c>rest</c>): the endpoints REST DSL services consume from. It does not serve HTTP itself: the consumer
/// is created by a component implementing <see cref="Spi.IRestConsumerFactory"/> (see <see cref="RestEndpoint"/>).
/// <para>URI: <c>rest:method:path[:uriTemplate][?options]</c>, e.g. <c>rest:get:/api/orders:/{id}?produces=application/json</c>.</para>
/// </summary>
public sealed class RestComponent : ComponentBase
{
    /// <inheritdoc />
    protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters)
    {
        var colon = remaining.IndexOf(':');
        if (colon <= 0)
        {
            throw new ArgumentException("rest endpoint must be rest:method:path[:uriTemplate], e.g. rest:get:/api/orders:/{id}");
        }

        var method = remaining[..colon].ToUpperInvariant();
        var path = remaining[(colon + 1)..];
        string? uriTemplate = null;
        var second = path.IndexOf(':');
        if (second >= 0)
        {
            uriTemplate = path[(second + 1)..];
            path = path[..second];
        }

        if (path.Length == 0 && string.IsNullOrEmpty(uriTemplate))
        {
            throw new ArgumentException("rest endpoint requires a path, e.g. rest:get:/api/orders");
        }

        return new RestEndpoint(uri, this, method, path, string.IsNullOrEmpty(uriTemplate) ? null : uriTemplate);
    }
}
