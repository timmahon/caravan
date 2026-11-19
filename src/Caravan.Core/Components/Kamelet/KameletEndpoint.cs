using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.Kamelet;

/// <summary>A <c>kamelet:templateId</c> endpoint: creates its route from the template on first use and calls it.</summary>
public sealed class KameletEndpoint : EndpointBase
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile string? _createdRouteId;

    internal KameletEndpoint(string uri, KameletComponent component, string templateId, string? routeId, IReadOnlyDictionary<string, object?> parameters)
        : base(uri, component, component.Context!)
    {
        TemplateId = templateId;
        RouteId = routeId;
        Parameters = parameters;
    }

    /// <summary>The id of the route template.</summary>
    public string TemplateId { get; }

    /// <summary>The id of the route to create, or null for a generated one (<c>{templateId}-{n}</c>).</summary>
    public string? RouteId { get; }

    /// <summary>The template parameters given in the URI.</summary>
    public IReadOnlyDictionary<string, object?> Parameters { get; }

    internal KameletComponent KameletComponent => (KameletComponent)Component!;

    /// <inheritdoc />
    public override IProducer CreateProducer() => new KameletProducer(this);

    /// <inheritdoc />
    public override IConsumer CreateConsumer(IProcessor processor) => new KameletConsumer(this, processor);

    /// <summary>Creates the route from the template unless done already; returns its id.</summary>
    internal ValueTask<string> EnsureRouteAsync(CancellationToken cancellationToken) => EnsureRouteAsync(null, cancellationToken);

    /// <summary>
    /// Creates the route from the template unless done already; returns its id. For a consumer the template must not consume from
    /// <c>kamelet:source</c>, and its sends to <c>kamelet:sink</c> go to the consuming route.
    /// </summary>
    /// <param name="consumer">For a consumer: called with the route id before the route is created (and started).</param>
    /// <param name="cancellationToken">Cancels the creation.</param>
    internal async ValueTask<string> EnsureRouteAsync(Action<string>? consumer, CancellationToken cancellationToken)
    {
        if (_createdRouteId is { } existing)
        {
            return existing;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_createdRouteId is { } raced)
            {
                return raced;
            }

            var templateContext = new RouteTemplateContext(Context) { IsKamelet = true };
            var template = Context.RouteTemplateDefinitions.FirstOrDefault(t => t.Id == TemplateId)
                ?? throw new FailedToCreateRouteFromTemplateException(RouteId, TemplateId, "No route template exists with id " + TemplateId);
            foreach (var parameter in template.Parameters)
            {
                if (Context.PropertiesComponent.ResolveProperty($"caravan.kamelet.{TemplateId}.{parameter.Name}") is { } configured)
                {
                    templateContext.SetParameter(parameter.Name, configured);
                }
            }

            foreach (var (name, value) in Parameters)
            {
                templateContext.SetParameter(name, value);
            }

            var routeId = RouteId ?? Context.NextNodeId(TemplateId + "-");
            if (consumer is not null)
            {
                if (Support.EndpointUri.Parse(template.Route?.FromUri ?? "kamelet:source") is { Scheme: "kamelet", Path: "source" })
                {
                    throw new FailedToCreateRouteFromTemplateException(routeId, TemplateId, "A Kamelet consumed with from must not consume from kamelet:source");
                }

                templateContext.Handler = route =>
                {
                    foreach (var to in route.AllNodes().OfType<Model.ToDefinition>())
                    {
                        if (Support.EndpointUri.Parse(to.Uri) is { Scheme: "kamelet", Path: "sink" })
                        {
                            to.Uri = $"kamelet:sink?routeId={Uri.EscapeDataString(routeId)}";
                        }
                    }
                };
            }

            consumer?.Invoke(routeId);
            _createdRouteId = await Context.AddRouteFromTemplateAsync(routeId, TemplateId, templateContext, cancellationToken).ConfigureAwait(false);
            return _createdRouteId;
        }
        finally
        {
            _lock.Release();
        }
    }
}
