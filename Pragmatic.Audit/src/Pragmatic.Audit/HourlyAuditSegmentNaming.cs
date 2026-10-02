using System.Globalization;

namespace Pragmatic.Audit;

/// <summary>
///     Default segmentation: one segment per UTC hour, named <c>yyyy-MM-ddTHH</c>.
/// </summary>
/// <remarks>
///     Hourly windows are predictable to verify against and bound how much has to be re-hashed to check
///     a range. Sizing by entry count instead would make segment boundaries depend on traffic, so the
///     same time range would verify differently on a busy day than on a quiet one.
/// </remarks>
public sealed class HourlyAuditSegmentNaming : IAuditSegmentNaming
{
    private const string Format = "yyyy-MM-ddTHH";

    /// <inheritdoc />
    public TimeSpan GracePeriod { get; init; } = TimeSpan.FromMinutes(5);

    public string SegmentFor(DateTimeOffset occurredAt)
        => occurredAt.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture);

    public DateTimeOffset WindowEnd(string segmentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segmentId);

        if (!DateTimeOffset.TryParseExact(
                segmentId, Format, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var start))
            throw new FormatException($"'{segmentId}' is not a segment id produced by {nameof(HourlyAuditSegmentNaming)}.");

        return start.AddHours(1);
    }
}
