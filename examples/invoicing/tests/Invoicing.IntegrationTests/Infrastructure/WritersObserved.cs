using System.Diagnostics.Metrics;
using Pragmatic.Endpoints.Diagnostics;

namespace Invoicing.IntegrationTests.Infrastructure;

/// <summary>
///     The writers the JSON responses were written by while it is alive, read from the
///     <c>pragmatic.endpoints.json_responses</c> counter: what the host did, not what a generated file says.
/// </summary>
/// <remarks>The suite runs its classes one at a time, so what is measured during a test is that test's.</remarks>
internal sealed class WritersObserved : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly List<string> _seen = [];

    public WritersObserved()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument, EndpointResponseMetrics.JsonResponses))
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
                if (tag.Key == EndpointResponseMetrics.WriterTag)
                    lock (_seen) _seen.Add((string)tag.Value!);
        });
        _listener.Start();
    }

    /// <summary><c>generated</c> or <c>serializer</c>, once per response.</summary>
    public IReadOnlyList<string> Seen
    {
        get
        {
            lock (_seen) return [.. _seen];
        }
    }

    public void Dispose() => _listener.Dispose();
}
