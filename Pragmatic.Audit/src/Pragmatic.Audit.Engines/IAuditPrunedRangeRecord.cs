namespace Pragmatic.Audit.Engines;

/// <summary>A declared gap as both engines store it: <see cref="PrunedRange" />'s fields, its instant in UTC ticks.</summary>
internal interface IAuditPrunedRangeRecord
{
    string FromSegmentId { get; set; }

    string UntilSegmentId { get; set; }

    long PrunedAt { get; set; }

    byte[] LinkHash { get; set; }
}
