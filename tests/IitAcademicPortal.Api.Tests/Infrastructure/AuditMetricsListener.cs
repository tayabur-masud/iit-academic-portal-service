using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using IitAcademicPortal.Application.Auditing;

namespace IitAcademicPortal.Api.Tests.Infrastructure;

/// <summary>Counts increments of one host's audit counters by instrument name, ignoring other hosts in the process.</summary>
public sealed class AuditMetricsListener : IDisposable
{
    private readonly MeterListener listener = new();
    private readonly ConcurrentDictionary<string, long> totals = new();

    public AuditMetricsListener(AuditMetrics source)
    {
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == AuditMetrics.MeterName && ReferenceEquals(instrument.Meter.Scope, source))
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
            totals.AddOrUpdate(instrument.Name, value, (_, current) => current + value));
        listener.Start();
    }

    public long Total(string instrumentName) => totals.GetValueOrDefault(instrumentName);

    public void Dispose() => listener.Dispose();
}
