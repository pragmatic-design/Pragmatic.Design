namespace Pragmatic.Temporal.EFCore.Tests;

/// <summary>Event row for query-extension tests. Timestamp is a UTC instant.</summary>
public class QueryEvent
{
    public int Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
