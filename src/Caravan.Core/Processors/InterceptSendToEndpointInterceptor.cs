using Caravan.Model;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Processors;

/// <summary>
/// The context-wide interceptor of one <see cref="InterceptSendToEndpointDefinition"/> (Camel's <c>InterceptSendToEndpointProcessor</c>).
/// Its block and after-endpoint are built once, with a route context of their own, and live as a context service.
/// </summary>
internal sealed class InterceptSendToEndpointInterceptor : ServiceSupport, ISendToEndpointInterceptor
{
    private readonly ICaravanContext _context;
    private readonly RouteContext _routeContext;
    private readonly IProcessor _block;
    private readonly SendProcessor? _after;

    public InterceptSendToEndpointInterceptor(ICaravanContext context, InterceptSendToEndpointDefinition definition)
    {
        _context = context;
        Definition = definition;
        var id = definition.Id ??= context.NextNodeId(definition.ShortName);
        _routeContext = new RouteContext(context, new RouteDefinition(definition.Uri) { Id = id }, id);
        _block = definition.CreateProcessor(_routeContext);
        _routeContext.AddService(_block);
        if (definition.AfterUri is { } afterUri)
        {
            _after = new SendProcessor(context.GetEndpoint(afterUri));
            _routeContext.AddService(_after);
        }
    }

    public InterceptSendToEndpointDefinition Definition { get; }

    public bool Matches(IEndpoint endpoint) => EndpointHelper.MatchEndpoint(_context, endpoint.EndpointUri, Definition.Uri);

    public async ValueTask SendAsync(Exchange exchange, IEndpoint endpoint, IProcessor next)
    {
        var active = exchange.GetProperty(ExchangePropertyKeys.InterceptSendToEndpointActive) as object[];
        if (active is not null && active.Contains(this))
        {
            // A send made by our own block: not intercepted again.
            await next.ProcessAsync(exchange).ConfigureAwait(false);
            return;
        }

        exchange.Message.SetHeader(CaravanHeaders.InterceptedEndpoint, endpoint.EndpointUri);
        var matched = Definition.When is null || Definition.When.Matches(exchange);
        if (matched)
        {
            var flags = RoutingFlags.Capture(exchange);
            exchange.SetProperty(ExchangePropertyKeys.InterceptSendToEndpointActive, active is null ? new object[] { this } : [.. active, this]);
            try
            {
                await _block.ProcessAsync(exchange).ConfigureAwait(false);
            }
            finally
            {
                if (active is null)
                {
                    exchange.RemoveProperty(ExchangePropertyKeys.InterceptSendToEndpointActive);
                }
                else
                {
                    exchange.SetProperty(ExchangePropertyKeys.InterceptSendToEndpointActive, active);
                }
            }

            if (flags.Interrupted(exchange) || Definition.SkipSendToOriginalEndpoint)
            {
                return;
            }
        }

        var beforeSend = RoutingFlags.Capture(exchange);
        await next.ProcessAsync(exchange).ConfigureAwait(false);
        if (_after is not null && !beforeSend.Interrupted(exchange))
        {
            await _after.ProcessAsync(exchange).ConfigureAwait(false);
        }
    }

    public override string ToString() => $"InterceptSendToEndpoint[{Definition.Uri}]";

    protected override async Task DoStartAsync(CancellationToken cancellationToken)
    {
        foreach (var service in _routeContext.Services)
        {
            await StartServiceAsync(service, cancellationToken).ConfigureAwait(false);
        }
    }

    protected override async Task DoStopAsync(CancellationToken cancellationToken)
    {
        foreach (var service in _routeContext.Services.Reverse())
        {
            await StopServiceAsync(service, cancellationToken).ConfigureAwait(false);
        }
    }
}
