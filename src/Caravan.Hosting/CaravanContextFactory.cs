using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Caravan.Hosting;

/// <summary>Creates the context registered by <c>AddCaravan</c>.</summary>
internal static class CaravanContextFactory
{
    public static CaravanContext Create(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<CaravanOptions>>().Value;
        var setup = services.GetRequiredService<CaravanSetup>();

        // The registry falls back to the service provider; its IConfiguration becomes a properties source.
        var context = new CaravanContext(services)
        {
            LoggerFactory = services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance,
        };
        if (options.UseServiceTimeProvider && services.GetService<TimeProvider>() is { } timeProvider)
        {
            context.TimeProvider = timeProvider;
        }

        Apply(options, context);
        foreach (var (scheme, factory) in setup.Components)
        {
            context.AddComponent(scheme, factory(services));
        }

        foreach (var source in setup.ParameterSources)
        {
            context.RouteTemplateParameterSources.Add(source(services));
        }

        foreach (var configure in setup.Configurers)
        {
            configure(services, context);
        }

        foreach (var routes in setup.Routes)
        {
            context.AddRoutes(routes(services));
        }

        return context;
    }

    private static void Apply(CaravanOptions options, CaravanContext context)
    {
        if (!string.IsNullOrWhiteSpace(options.Name))
        {
            context.Name = options.Name;
        }

        context.AutoStartup = options.AutoStartup;
        if (options.StreamCaching is { } streamCaching)
        {
            context.StreamCaching.Enabled = streamCaching;
        }

        if (options.ShutdownTimeout is { } timeout)
        {
            context.ShutdownStrategy.Timeout = timeout;
        }

        context.RouteFilterIncludePattern = options.RouteFilterIncludePattern;
        context.RouteFilterExcludePattern = options.RouteFilterExcludePattern;
        context.Tracing = options.TracingEnabled;
        context.Tracer.Standby = options.TracingStandby;
        context.Tracer.TracePattern = options.TracePattern;
        context.BacklogTracer.Enabled = options.BacklogTracing;
        context.MessageHistory = options.MessageHistory;
        context.AllowUseOriginalMessage = options.AllowUseOriginalMessage;
        context.Telemetry.TracingEnabled = options.OpenTelemetryTracing;
        context.Telemetry.MetricsEnabled = options.OpenTelemetryMetrics;
        context.Telemetry.TraceProcessors = options.TraceProcessors;
        context.Telemetry.PropagateTraceContext = options.PropagateTraceContext;
        if (options.StatisticsLevel is { } level)
        {
            context.Management.StatisticsLevel = level;
        }
    }
}
