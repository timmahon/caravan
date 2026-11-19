using Caravan.Model;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Processors;

/// <summary>Runs processors in sequence. Stops when the exchange failed, was stopped or marked rollback-only.</summary>
public sealed class Pipeline(IReadOnlyList<IProcessor> processors) : INavigableProcessor
{
    public IReadOnlyList<IProcessor> Next { get; } = processors;

    public async ValueTask ProcessAsync(Exchange exchange)
    {
        foreach (var processor in Next)
        {
            if (!ContinueProcessing(exchange))
            {
                return;
            }

            await processor.ProcessAsync(exchange).ConfigureAwait(false);
        }
    }

    public static bool ContinueProcessing(Exchange exchange) =>
        !exchange.IsFailed && !exchange.IsRouteStop && !exchange.IsRollbackOnly;

    public override string ToString() => $"Pipeline[{string.Join(", ", Next)}]";
}

/// <summary>
/// Wraps each node in a route. Records exceptions thrown by the node on the exchange and skips the
/// node when the exchange has been stopped. Feature-specific behaviour is added through intercept strategies.
/// </summary>
public sealed class Channel(RouteContext routeContext, ProcessorDefinition definition, IProcessor next) : INavigableProcessor
{
    public RouteContext RouteContext { get; } = routeContext;

    public ProcessorDefinition Definition { get; } = definition;

    public IProcessor Target { get; } = next;

    public IReadOnlyList<IProcessor> Next => [Target];

    public async ValueTask ProcessAsync(Exchange exchange)
    {
        if (exchange.IsRouteStop)
        {
            return;
        }

        try
        {
            await Target.ProcessAsync(exchange).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            exchange.Exception = e;
        }
    }

    public override string ToString() => $"Channel[{Definition}]";
}

/// <summary>Sends the exchange to an endpoint (the <c>to</c> EIP).</summary>
public sealed class SendProcessor(IEndpoint endpoint, ExchangePattern? pattern = null) : ServiceSupport, IProcessor
{
    private IProducer? _producer;

    public IEndpoint Endpoint { get; } = endpoint;

    public ExchangePattern? Pattern { get; } = pattern;

    public async ValueTask ProcessAsync(Exchange exchange)
    {
        var producer = _producer ?? throw new InvalidOperationException($"{this} is not started");
        var original = exchange.Pattern;
        if (Pattern is { } p)
        {
            exchange.Pattern = p;
        }

        exchange.SetProperty(ExchangePropertyKeys.ToEndpoint, Endpoint.EndpointUri);
        var context = exchange.Context;
        EventHelper.Notify(context, CaravanEventType.ExchangeSending, exchange, Endpoint);
        var start = context.TimeProvider.GetTimestamp();
        try
        {
            await producer.ProcessAsync(exchange).ConfigureAwait(false);
        }
        finally
        {
            exchange.Pattern = original;
            EventHelper.Notify(
                context,
                new CaravanEvent(CaravanEventType.ExchangeSent, context)
                {
                    Exchange = exchange,
                    Endpoint = Endpoint,
                    Duration = context.TimeProvider.GetElapsedTime(start),
                });
        }
    }

    protected override async Task DoStartAsync(CancellationToken cancellationToken)
    {
        await Endpoint.StartAsync(cancellationToken).ConfigureAwait(false);
        _producer = Endpoint.CreateProducer();
        await _producer.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task DoStopAsync(CancellationToken cancellationToken)
    {
        if (_producer is not null)
        {
            await _producer.StopAsync(cancellationToken).ConfigureAwait(false);
            _producer = null;
        }
    }

    public override string ToString() => $"SendTo[{Endpoint.EndpointUri}]";
}

/// <summary>Sets the message body from an expression (<c>setBody</c> / <c>transform</c>).</summary>
public sealed class SetBodyProcessor(IExpression expression) : IProcessor
{
    public IExpression Expression { get; } = expression;

    public ValueTask ProcessAsync(Exchange exchange)
    {
        exchange.Message.Body = Expression.Evaluate(exchange);
        return ValueTask.CompletedTask;
    }

    public override string ToString() => $"SetBody[{Expression}]";
}

/// <summary>Sets a header from an expression.</summary>
public sealed class SetHeaderProcessor(string name, IExpression expression) : IProcessor
{
    public string Name { get; } = name;

    public IExpression Expression { get; } = expression;

    public ValueTask ProcessAsync(Exchange exchange)
    {
        exchange.Message.SetHeader(Name, Expression.Evaluate(exchange));
        return ValueTask.CompletedTask;
    }

    public override string ToString() => $"SetHeader[{Name}, {Expression}]";
}

/// <summary>Helpers for raising events.</summary>
public static class EventHelper
{
    public static void Notify(ICaravanContext context, CaravanEventType type, Exchange? exchange = null, IEndpoint? endpoint = null, string? routeId = null)
    {
        if (context.EventNotifiers.Count == 0)
        {
            return;
        }

        Notify(context, new CaravanEvent(type, context) { Exchange = exchange, Endpoint = endpoint, RouteId = routeId });
    }

    public static void Notify(ICaravanContext context, CaravanEvent evt)
    {
        foreach (var notifier in context.EventNotifiers)
        {
            try
            {
                if (notifier.IsEnabled(evt))
                {
                    notifier.Notify(evt);
                }
            }
            catch
            {
                // Event notifiers must never break routing.
            }
        }
    }
}
