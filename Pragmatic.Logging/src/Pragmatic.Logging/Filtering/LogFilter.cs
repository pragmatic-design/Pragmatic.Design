using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Filtering;

/// <summary>
/// Represents a conditional filter that can be applied to log entries.
/// </summary>
public abstract class LogFilter
{
    /// <summary>
    /// Gets the filter name for debugging and configuration.
    /// </summary>
    public string Name { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the filter priority. Higher priority filters are evaluated first.
    /// </summary>
    public int Priority { get; protected set; }

    /// <summary>
    /// Determines if a log entry should be processed by this filter.
    /// </summary>
    /// <param name="logEntry">The log entry to evaluate</param>
    /// <param name="context">Additional context for filtering</param>
    /// <returns>The filter decision</returns>
    public abstract LogFilterResult ShouldLog(LogEntry logEntry, LogFilterContext context);
}

/// <summary>
/// Result of a log filter evaluation.
/// </summary>
public enum LogFilterResult
{
    /// <summary>Continue to next filter in chain</summary>
    Continue,
    /// <summary>Allow this log entry (skip remaining filters)</summary>
    Allow,
    /// <summary>Reject this log entry (skip remaining filters)</summary>
    Reject
}

/// <summary>
/// Context information available to filters.
/// </summary>
public sealed class LogFilterContext
{
    /// <summary>
    /// HTTP context if available (ASP.NET Core scenarios)
    /// </summary>
    public object? HttpContext { get; set; }

    /// <summary>
    /// Current user identifier if available
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Request correlation ID if available
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Current request path if available
    /// </summary>
    public string? RequestPath { get; set; }

    /// <summary>
    /// HTTP method if available (GET, POST, etc.)
    /// </summary>
    public string? HttpMethod { get; set; }

    /// <summary>
    /// Current environment name (Development, Production, etc.)
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>
    /// Custom properties that can be used by filters
    /// </summary>
    public Dictionary<string, object?> Properties { get; } = new();
}

/// <summary>
/// Chain of filters that can be applied to log entries.
/// </summary>
public sealed class LogFilterChain
{
    private readonly LogFilter[] _filters;

    public LogFilterChain(IEnumerable<LogFilter> filters)
    {
        _filters = filters.OrderByDescending(f => f.Priority).ToArray();
    }

    /// <summary>
    /// Evaluates the filter chain against a log entry.
    /// </summary>
    /// <param name="logEntry">The log entry to evaluate</param>
    /// <param name="context">Filter context</param>
    /// <returns>True if the log entry should be processed</returns>
    public bool ShouldLog(LogEntry logEntry, LogFilterContext context)
    {
        foreach (var filter in _filters)
        {
            var result = filter.ShouldLog(logEntry, context);
            switch (result)
            {
                case LogFilterResult.Allow:
                    return true;
                case LogFilterResult.Reject:
                    return false;
                case LogFilterResult.Continue:
                    continue;
                default:
#pragma warning disable CA2208
                    throw new ArgumentOutOfRangeException(nameof(result), result, "Unknown filter result");
#pragma warning restore CA2208
            }
        }

        // If no filter made a decision, default to allowing the log
        return true;
    }
}
