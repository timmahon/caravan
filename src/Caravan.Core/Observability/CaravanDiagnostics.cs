using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Caravan.Observability;

/// <summary>
/// The <see cref="System.Diagnostics.ActivitySource"/> and <see cref="System.Diagnostics.Metrics.Meter"/> through which Caravan publishes its
/// traces and metrics (OpenTelemetry-compatible, dependency free). Subscribe with <c>AddSource("Caravan")</c> / <c>AddMeter("Caravan")</c>, or
/// <c>AddCaravanInstrumentation()</c> from the <c>Caravan.OpenTelemetry</c> package. Nothing is recorded while no listener is attached.
/// </summary>
public static class CaravanDiagnostics
{
    /// <summary>The name of the activity source (<c>Caravan</c>).</summary>
    public const string ActivitySourceName = "Caravan";

    /// <summary>The name of the meter (<c>Caravan</c>).</summary>
    public const string MeterName = "Caravan";

    /// <summary>The version of the activity source and meter (the Caravan.Core assembly's informational version).</summary>
    public static readonly string Version = typeof(CaravanDiagnostics).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(CaravanDiagnostics).Assembly.GetName().Version?.ToString()
        ?? "1.0.0";

    /// <summary>The source of Caravan's spans: one per exchange per route, per send to an endpoint and (optionally) per node.</summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);

    /// <summary>The meter of Caravan's metrics (see <see cref="CaravanSemanticConventions"/> for the instrument names).</summary>
    public static readonly Meter Meter = new(MeterName, Version);
}
