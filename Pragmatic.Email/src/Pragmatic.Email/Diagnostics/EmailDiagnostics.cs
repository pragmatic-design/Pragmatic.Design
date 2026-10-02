using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Pragmatic.Email.Diagnostics;

/// <summary>
///     OpenTelemetry diagnostics for Pragmatic.Email.
/// </summary>
public static class EmailDiagnostics
{
    public const string SourceName = "Pragmatic.Email";

    public static readonly ActivitySource ActivitySource = new(SourceName, "1.0.0");
    // Static Meter lifetime matches the process lifetime — intentionally not disposed.
    // OpenTelemetry SDKs manage meter provider shutdown independently.
    public static readonly Meter Meter = new(SourceName, "1.0.0");

    public static readonly Counter<long> EmailsSent = Meter.CreateCounter<long>("pragmatic.email.sent", "emails");
    public static readonly Counter<long> EmailsFailed = Meter.CreateCounter<long>("pragmatic.email.failed", "emails");
    public static readonly Histogram<double> SendDuration = Meter.CreateHistogram<double>("pragmatic.email.send.duration", "ms");
    public static readonly Counter<long> ConnectionsCreated = Meter.CreateCounter<long>("pragmatic.email.connections.created");
    public static readonly Counter<long> ConnectionsRecycled = Meter.CreateCounter<long>("pragmatic.email.connections.recycled");
}
