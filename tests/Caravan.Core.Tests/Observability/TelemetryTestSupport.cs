using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Caravan.Observability;

namespace Caravan.Core.Tests.Observability;

/// <summary>Collects the Caravan spans of one context (tests run in parallel: other contexts' spans are not sampled).</summary>
public sealed class ActivityCollector : IDisposable
{
    private readonly ActivityListener _listener;

    public ActivityCollector(string contextName)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CaravanDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
            {
                foreach (var tag in options.Tags ?? [])
                {
                    if (tag.Key == CaravanSemanticConventions.ContextName && Equals(tag.Value, contextName))
                    {
                        return ActivitySamplingResult.AllDataAndRecorded;
                    }
                }

                return ActivitySamplingResult.None;
            },
            ActivityStopped = a => Stopped.Enqueue(a),
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public ConcurrentQueue<Activity> Stopped { get; } = new();

    public IReadOnlyList<Activity> Spans => [.. Stopped];

    public Activity Single(string name) => Assert.Single(Spans, a => a.DisplayName == name);

    public void Dispose() => _listener.Dispose();
}

/// <summary>Collects the Caravan measurements of one context.</summary>
public sealed class MeasurementCollector : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly string _contextName;

    public MeasurementCollector(string contextName)
    {
        _contextName = contextName;
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == CaravanDiagnostics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((i, v, tags, _) => Record(i, v, tags));
        _listener.SetMeasurementEventCallback<int>((i, v, tags, _) => Record(i, v, tags));
        _listener.SetMeasurementEventCallback<double>((i, v, tags, _) => Record(i, v, tags));
        _listener.Start();
    }

    public ConcurrentQueue<(string Name, double Value, Dictionary<string, object?> Tags)> Measurements { get; } = new();

    public void RecordObservable() => _listener.RecordObservableInstruments();

    /// <summary>The sum of the values of an instrument whose tags contain <paramref name="tag"/>.</summary>
    public double Sum(string name, string? routeId = null) =>
        Measurements.Where(m => m.Name == name && (routeId is null || Equals(m.Tags.GetValueOrDefault(CaravanSemanticConventions.RouteId), routeId)))
            .Sum(m => m.Value);

    public int Count(string name, string? routeId = null) =>
        Measurements.Count(m => m.Name == name && (routeId is null || Equals(m.Tags.GetValueOrDefault(CaravanSemanticConventions.RouteId), routeId)));

    public void Dispose() => _listener.Dispose();

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var dictionary = new Dictionary<string, object?>();
        foreach (var tag in tags)
        {
            dictionary[tag.Key] = tag.Value;
        }

        if (Equals(dictionary.GetValueOrDefault(CaravanSemanticConventions.ContextName), _contextName))
        {
            Measurements.Enqueue((instrument.Name, value, dictionary));
        }
    }
}
