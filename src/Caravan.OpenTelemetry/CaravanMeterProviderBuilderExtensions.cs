using Caravan.Observability;

namespace OpenTelemetry.Metrics;

/// <summary>Subscribes an OpenTelemetry meter provider to Caravan's metrics.</summary>
public static class CaravanMeterProviderBuilderExtensions
{
    /// <summary>
    /// Adds Caravan's meter (<see cref="CaravanDiagnostics.MeterName"/>): exchange counters, durations and inflight per route, started routes and
    /// send durations per endpoint (see <see cref="CaravanSemanticConventions"/>).
    /// </summary>
    /// <param name="builder">The meter provider builder.</param>
    /// <returns>The builder.</returns>
    public static MeterProviderBuilder AddCaravanInstrumentation(this MeterProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddMeter(CaravanDiagnostics.MeterName);
    }
}
