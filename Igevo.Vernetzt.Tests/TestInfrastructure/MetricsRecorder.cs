using System.Diagnostics.Metrics;
using Igevo.Vernetzt.Infrastructure.Observability;

namespace Igevo.Vernetzt.Tests.TestInfrastructure;

/// <summary>
/// Records the measurements of one <see cref="VernetztMetrics"/> instance. Each recorder has its own
/// meter factory, so tests running in parallel do not see each other's measurements.
/// </summary>
public sealed class MetricsRecorder : IDisposable
{
    private readonly TestMeterFactory _factory = new();
    private readonly MeterListener _listener = new();
    private readonly List<(string Name, double Value, Dictionary<string, string?> Tags)> _measurements = [];
    private readonly Lock _gate = new();

    public MetricsRecorder()
    {
        Metrics = new VernetztMetrics(_factory);

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter.Scope, _factory))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.Start();
    }

    public VernetztMetrics Metrics { get; }

    /// <summary>Sum of all measurements of a counter whose tags contain the given pairs.</summary>
    public double Sum(string name, params (string Key, string Value)[] tags)
    {
        lock (_gate)
        {
            return _measurements
                .Where(m => m.Name == name && tags.All(t => m.Tags.TryGetValue(t.Key, out var v) && v == t.Value))
                .Sum(m => m.Value);
        }
    }

    /// <summary>Collects the observable gauges once and returns the current value, or null if none was reported.</summary>
    public double? ObserveGauge(string name)
    {
        lock (_gate)
        {
            _measurements.RemoveAll(m => m.Name == name);
        }

        _listener.RecordObservableInstruments();

        lock (_gate)
        {
            var values = _measurements.Where(m => m.Name == name).Select(m => (double?)m.Value).ToList();
            return values.LastOrDefault();
        }
    }

    public void Dispose()
    {
        _listener.Dispose();
        _factory.Dispose();
    }

    private void Record<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        where T : struct
    {
        var tagMap = new Dictionary<string, string?>();
        foreach (var tag in tags)
        {
            tagMap[tag.Key] = tag.Value?.ToString();
        }

        lock (_gate)
        {
            _measurements.Add((instrument.Name, Convert.ToDouble(value), tagMap));
        }
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = [];

        public Meter Create(MeterOptions options)
        {
            options.Scope = this;
            var meter = new Meter(options);
            _meters.Add(meter);
            return meter;
        }

        public void Dispose()
        {
            foreach (var meter in _meters)
            {
                meter.Dispose();
            }
        }
    }
}
