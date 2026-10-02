using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Privacy.Audit;

/// <summary>
/// Builder for constructing audit queries.
/// </summary>
public sealed class AuditQueryBuilder
{
    private readonly List<IAuditFilter> _filters = new();
    private readonly List<AuditSortCriteria> _sortCriteria = new();
    private int? _skip;
    private int? _take;

    /// <summary>
    /// Filters entries by event type.
    /// </summary>
    /// <param name="eventType">The event type to filter by</param>
    /// <returns>The query builder for chaining</returns>
    public AuditQueryBuilder WhereEventType(AuditEventType eventType)
    {
        _filters.Add(new EventTypeFilter(eventType));
        return this;
    }

    /// <summary>
    /// Filters entries by compliance standard.
    /// </summary>
    /// <param name="standard">The compliance standard to filter by</param>
    /// <returns>The query builder for chaining</returns>
    public AuditQueryBuilder WhereComplianceStandard(ComplianceStandard standard)
    {
        _filters.Add(new ComplianceStandardFilter(standard));
        return this;
    }

    /// <summary>
    /// Filters entries by severity level.
    /// </summary>
    /// <param name="severity">The minimum severity level</param>
    /// <returns>The query builder for chaining</returns>
    public AuditQueryBuilder WhereSeverityAtLeast(AuditSeverity severity)
    {
        _filters.Add(new SeverityFilter(severity, SeverityComparison.AtLeast));
        return this;
    }

    /// <summary>
    /// Filters entries by user ID.
    /// </summary>
    /// <param name="userId">The user ID to filter by</param>
    /// <returns>The query builder for chaining</returns>
    public AuditQueryBuilder WhereUserId(string userId)
    {
        _filters.Add(new UserIdFilter(userId));
        return this;
    }

    /// <summary>
    /// Filters entries by correlation ID.
    /// </summary>
    /// <param name="correlationId">The correlation ID to filter by</param>
    /// <returns>The query builder for chaining</returns>
    public AuditQueryBuilder WhereCorrelationId(string correlationId)
    {
        _filters.Add(new CorrelationIdFilter(correlationId));
        return this;
    }

    /// <summary>
    /// Filters entries by time range.
    /// </summary>
    /// <param name="from">Start time (inclusive)</param>
    /// <param name="until">End time (exclusive)</param>
    /// <returns>The query builder for chaining</returns>
    public AuditQueryBuilder WhereTimestampBetween(DateTime from, DateTime until)
    {
        _filters.Add(new TimestampRangeFilter(from, until));
        return this;
    }

    /// <summary>
    /// Filters entries by category name pattern.
    /// </summary>
    /// <param name="pattern">The pattern to match (supports wildcards)</param>
    /// <returns>The query builder for chaining</returns>
    public AuditQueryBuilder WhereCategoryMatches(string pattern)
    {
        _filters.Add(new CategoryPatternFilter(pattern));
        return this;
    }

    /// <summary>
    /// Orders results by timestamp.
    /// </summary>
    /// <param name="ascending">Whether to sort in ascending order</param>
    /// <returns>The query builder for chaining</returns>
    public AuditQueryBuilder OrderByTimestamp(bool ascending = true)
    {
        _sortCriteria.Add(new AuditSortCriteria(AuditSortField.Timestamp, ascending));
        return this;
    }

    /// <summary>
    /// Orders results by severity (descending by default).
    /// </summary>
    /// <param name="ascending">Whether to sort in ascending order</param>
    /// <returns>The query builder for chaining</returns>
    public AuditQueryBuilder OrderBySeverity(bool ascending = false)
    {
        _sortCriteria.Add(new AuditSortCriteria(AuditSortField.Severity, ascending));
        return this;
    }

    /// <summary>
    /// Skips the specified number of entries (for pagination).
    /// </summary>
    /// <param name="count">Number of entries to skip</param>
    /// <returns>The query builder for chaining</returns>
    public AuditQueryBuilder Skip(int count)
    {
        _skip = count;
        return this;
    }

    /// <summary>
    /// Takes only the specified number of entries (for pagination).
    /// </summary>
    /// <param name="count">Number of entries to take</param>
    /// <returns>The query builder for chaining</returns>
    public AuditQueryBuilder Take(int count)
    {
        _take = count;
        return this;
    }

    /// <summary>
    /// Builds the audit query specification.
    /// </summary>
    /// <returns>The audit query specification</returns>
    internal AuditQuerySpecification Build()
    {
        return new AuditQuerySpecification
        {
            Filters = _filters.ToArray(),
            SortCriteria = _sortCriteria.ToArray(),
            Skip = _skip,
            Take = _take
        };
    }
}

/// <summary>
/// Internal query specification.
/// </summary>
internal sealed class AuditQuerySpecification
{
    public IAuditFilter[] Filters { get; set; } = Array.Empty<IAuditFilter>();
    public AuditSortCriteria[] SortCriteria { get; set; } = Array.Empty<AuditSortCriteria>();
    public int? Skip { get; set; }
    public int? Take { get; set; }
}

/// <summary>
/// Interface for audit entry filters.
/// </summary>
public interface IAuditFilter
{
    /// <summary>
    /// Tests whether an audit entry matches this filter.
    /// </summary>
    /// <param name="entry">The entry to test</param>
    /// <returns>True if the entry matches</returns>
    bool Matches(AuditEntry entry);
}

/// <summary>
/// Sort criteria for audit queries.
/// </summary>
public sealed class AuditSortCriteria(AuditSortField field, bool ascending)
{
    public AuditSortField Field { get; } = field;
    public bool Ascending { get; } = ascending;
}

/// <summary>
/// Fields available for sorting audit entries.
/// </summary>
public enum AuditSortField
{
    /// <summary>Sort by timestamp.</summary>
    Timestamp,
    /// <summary>Sort by severity level.</summary>
    Severity,
    /// <summary>Sort by event type.</summary>
    EventType,
    /// <summary>Sort by user ID.</summary>
    UserId
}

/// <summary>
/// Severity comparison modes.
/// </summary>
public enum SeverityComparison
{
    /// <summary>Exact match.</summary>
    Exact,
    /// <summary>At least this severity level.</summary>
    AtLeast,
    /// <summary>At most this severity level.</summary>
    AtMost
}

// Filter implementations
internal sealed class EventTypeFilter(AuditEventType eventType) : IAuditFilter
{
    public bool Matches(AuditEntry entry) => entry.EventType == eventType;
}

internal sealed class ComplianceStandardFilter(ComplianceStandard standard) : IAuditFilter
{
    public bool Matches(AuditEntry entry) => entry.ComplianceStandard == standard;
}

internal sealed class SeverityFilter(AuditSeverity severity, SeverityComparison comparison) : IAuditFilter
{
    public bool Matches(AuditEntry entry)
    {
        return comparison switch
        {
            SeverityComparison.Exact => entry.Severity == severity,
            SeverityComparison.AtLeast => entry.Severity >= severity,
            SeverityComparison.AtMost => entry.Severity <= severity,
            _ => false
        };
    }
}

internal sealed class UserIdFilter(string userId) : IAuditFilter
{
    public bool Matches(AuditEntry entry) => string.Equals(entry.UserId, userId, StringComparison.Ordinal);
}

internal sealed class CorrelationIdFilter(string correlationId) : IAuditFilter
{
    public bool Matches(AuditEntry entry) => string.Equals(entry.CorrelationId, correlationId, StringComparison.Ordinal);
}

internal sealed class TimestampRangeFilter(DateTime from, DateTime until) : IAuditFilter
{
    public bool Matches(AuditEntry entry) => entry.Timestamp >= from && entry.Timestamp < until;
}

internal sealed class CategoryPatternFilter(string pattern) : IAuditFilter
{
    public bool Matches(AuditEntry entry)
    {
        if (entry.CategoryName == null)
            return false;

        // Simple wildcard matching - could be enhanced with regex
        if (pattern.Contains('*'))
        {
            var regex = new System.Text.RegularExpressions.Regex(
                "^" + pattern.Replace("*", ".*") + "$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return regex.IsMatch(entry.CategoryName);
        }

        return entry.CategoryName.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }
}