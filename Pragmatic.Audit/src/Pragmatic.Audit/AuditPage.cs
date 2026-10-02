namespace Pragmatic.Audit;

/// <summary>A page of audit entries.</summary>
/// <param name="Entries">The matching entries, newest first.</param>
/// <param name="TotalCount">How many entries match the query in total.</param>
public sealed record AuditPage(IReadOnlyList<AuditEntry> Entries, long TotalCount)
{
    /// <summary>An empty page.</summary>
    public static AuditPage Empty { get; } = new([], 0);
}
