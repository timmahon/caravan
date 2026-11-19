using System.Collections.Concurrent;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.Kamelet;

/// <summary>
/// The <c>kamelet</c> component (Camel's <c>kamelet</c>, producer side): <c>to("kamelet:templateId[/routeId]?param=value")</c> calls a route created
/// from the route template <c>templateId</c>, once per distinct URI (so per parameter set), the first time the endpoint is used. The template
/// consumes from <c>kamelet:source</c>; the call is synchronous like <c>direct</c>, so the exchange comes back to the caller when the template's
/// route ends — <c>to("kamelet:sink")</c> marks that end and does nothing. Parameters missing from the URI are read from the properties
/// <c>caravan.kamelet.{templateId}.{name}</c>, then the template's defaults.
/// <para>URIs: <c>kamelet:templateId[/routeId][?routeId=..&amp;param=value...]</c>, <c>kamelet:source</c> (template input), <c>kamelet:sink</c>.</para>
/// </summary>
/// <remarks>Consuming from a Kamelet (<c>from("kamelet:...")</c>) is not supported yet.</remarks>
public sealed class KameletComponent : ComponentBase
{
    private readonly ConcurrentDictionary<string, IProcessor> _sources = new(StringComparer.Ordinal);

    /// <inheritdoc />
    protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters)
    {
        var routeId = parameters.TryGetValue("routeId", out var id) ? id : null;
        parameters.Remove("routeId");
        switch (remaining)
        {
            case "source":
                parameters.Clear();
                return new KameletSourceEndpoint(uri, this, routeId ?? throw new ArgumentException("kamelet:source requires the routeId option"));
            case "sink":
                parameters.Clear();
                return new KameletSinkEndpoint(uri, this);
        }

        var slash = remaining.IndexOf('/', StringComparison.Ordinal);
        var templateId = slash < 0 ? remaining : remaining[..slash];
        if (slash >= 0)
        {
            routeId ??= remaining[(slash + 1)..];
        }

        if (string.IsNullOrEmpty(templateId))
        {
            throw new ArgumentException("kamelet endpoint requires a template id, e.g. kamelet:myTemplate?name=x");
        }

        var templateParameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in parameters)
        {
            templateParameters[key] = value;
        }

        parameters.Clear();
        return new KameletEndpoint(uri, this, templateId, string.IsNullOrEmpty(routeId) ? null : routeId, templateParameters);
    }

    internal void AddSource(string routeId, IProcessor processor)
    {
        if (!_sources.TryAdd(routeId, processor))
        {
            throw new InvalidOperationException($"Cannot add a 2nd consumer to kamelet:source of route {routeId}");
        }
    }

    internal void RemoveSource(string routeId, IProcessor processor) =>
        _sources.TryRemove(new KeyValuePair<string, IProcessor>(routeId, processor));

    internal IProcessor? GetSource(string routeId) => _sources.GetValueOrDefault(routeId);
}
