using System.Globalization;
using System.Security;
using System.Text;
using Caravan.Impl;
using Caravan.Spi;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Components.ControlBus;

/// <summary>Runs the action of a <c>controlbus</c> endpoint.</summary>
internal sealed class ControlBusProducer(ControlBusEndpoint endpoint) : ProducerBase(endpoint)
{
    private static readonly string[] Actions = ["start", "stop", "suspend", "resume", "restart", "status", "stats"];

    public static void ValidateAction(string? action)
    {
        if (action is null || !Actions.Contains(action, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"The controlbus action must be one of {string.Join(", ", Actions)}, was: {action ?? "(none)"}");
        }
    }

    public override async ValueTask ProcessAsync(Exchange exchange)
    {
        if (endpoint.Command == "language")
        {
            var text = ObjectHelper.ToText(exchange, exchange.Message.Body)
                ?? throw new InvalidPayloadException(exchange, typeof(string));
            var result = exchange.Context.ResolveLanguage(endpoint.Language!).CreateExpression(text).Evaluate(exchange);
            if (result is not null)
            {
                exchange.Message.Body = result;
            }

            return;
        }

        var routeId = endpoint.RouteId is null ? null : exchange.Context.ResolvePropertyPlaceholders(endpoint.RouteId);
        if (string.Equals(routeId, "current", StringComparison.Ordinal))
        {
            routeId = exchange.CurrentRouteId ?? exchange.FromRouteId;
        }

        var action = endpoint.Action!.ToLowerInvariant();
        if (endpoint.Async)
        {
            var context = exchange.Context;
            _ = Task.Run(async () =>
            {
                try
                {
                    await RunAsync(context, routeId, action).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    context.LoggerFactory.CreateLogger<ControlBusProducer>().LogWarning(e, "Error executing controlbus action {Action} on route {RouteId}", action, routeId);
                }
            });
            return;
        }

        var value = await RunAsync(exchange.Context, routeId, action).ConfigureAwait(false);
        if (value is not null)
        {
            exchange.Message.Body = value;
        }
    }

    private async Task<object?> RunAsync(ICaravanContext context, string? routeId, string action)
    {
        if (routeId is null)
        {
            return action switch
            {
                "status" => context is ServiceSupport service ? service.Status.ToString() : null,
                "stats" => ContextStats(context),
                _ => throw new ArgumentException($"The controlbus action {action} requires a routeId"),
            };
        }

        if (context.GetRoute(routeId) is null)
        {
            throw new ArgumentException($"No route with id: {routeId}");
        }

        switch (action)
        {
            case "start":
                await context.StartRouteAsync(routeId).ConfigureAwait(false);
                return null;
            case "stop":
                await context.StopRouteAsync(routeId).ConfigureAwait(false);
                return null;
            case "suspend":
                await context.SuspendRouteAsync(routeId).ConfigureAwait(false);
                return null;
            case "resume":
                await context.ResumeRouteAsync(routeId).ConfigureAwait(false);
                return null;
            case "restart":
                await context.StopRouteAsync(routeId).ConfigureAwait(false);
                if (endpoint.RestartDelay > TimeSpan.Zero)
                {
                    await Task.Delay(endpoint.RestartDelay, context.TimeProvider).ConfigureAwait(false);
                }

                await context.StartRouteAsync(routeId).ConfigureAwait(false);
                return null;
            case "status":
                return context.GetRouteStatus(routeId)?.ToString();
            default:
                return RouteStats(context.GetRoute(routeId)!);
        }
    }

    private static string RouteStats(IRoute route)
    {
        var builder = new StringBuilder();
        AppendRouteStats(builder, route);
        return builder.ToString();
    }

    private static void AppendRouteStats(StringBuilder builder, IRoute route)
    {
        var (total, completed, failed) = route is Route r ? (r.ExchangesTotal, r.ExchangesCompleted, r.ExchangesFailed) : (0L, 0L, 0L);
        builder.Append(CultureInfo.InvariantCulture, $"<routeStat id=\"{SecurityElement.Escape(route.Id)}\" state=\"{route.Status}\"")
            .Append(CultureInfo.InvariantCulture, $" exchangesInflight=\"{route.InflightCount}\" exchangesTotal=\"{total}\"")
            .Append(CultureInfo.InvariantCulture, $" exchangesCompleted=\"{completed}\" exchangesFailed=\"{failed}\"/>");
    }

    private static string ContextStats(ICaravanContext context)
    {
        var routes = context.Routes;
        var builder = new StringBuilder();
        var status = context is ServiceSupport service ? service.Status.ToString() : string.Empty;
        builder.Append(CultureInfo.InvariantCulture, $"<camelContextStat id=\"{SecurityElement.Escape(context.Name)}\" state=\"{status}\"")
            .Append(CultureInfo.InvariantCulture, $" exchangesInflight=\"{routes.Sum(r => r.InflightCount)}\"")
            .Append(CultureInfo.InvariantCulture, $" exchangesTotal=\"{routes.OfType<Route>().Sum(r => r.ExchangesTotal)}\"")
            .Append(CultureInfo.InvariantCulture, $" exchangesCompleted=\"{routes.OfType<Route>().Sum(r => r.ExchangesCompleted)}\"")
            .Append(CultureInfo.InvariantCulture, $" exchangesFailed=\"{routes.OfType<Route>().Sum(r => r.ExchangesFailed)}\">")
            .Append("<routeStats>");
        foreach (var route in routes)
        {
            AppendRouteStats(builder, route);
        }

        return builder.Append("</routeStats></camelContextStat>").ToString();
    }
}
