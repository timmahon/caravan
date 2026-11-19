using Caravan.Observability;

namespace OpenTelemetry.Trace;

/// <summary>Subscribes an OpenTelemetry tracer provider to Caravan's spans.</summary>
public static class CaravanTracerProviderBuilderExtensions
{
    /// <summary>
    /// Adds Caravan's activity source (<see cref="CaravanDiagnostics.ActivitySourceName"/>): a span per exchange per route, per send to an endpoint
    /// and, with <see cref="TelemetryOptions.TraceProcessors"/>, per node.
    /// </summary>
    /// <param name="builder">The tracer provider builder.</param>
    /// <returns>The builder.</returns>
    public static TracerProviderBuilder AddCaravanInstrumentation(this TracerProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddSource(CaravanDiagnostics.ActivitySourceName);
    }
}
