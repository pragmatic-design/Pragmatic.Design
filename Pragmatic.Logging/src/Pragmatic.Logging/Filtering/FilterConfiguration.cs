using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging.Filtering;

// Configuration classes are defined in IPragmaticLoggerProvider.cs

/// <summary>
/// Configuration for advanced filtering.
/// </summary>
public sealed class FilterConfiguration
{
    /// <summary>
    /// Gets or sets the list of filters to apply.
    /// </summary>
    public List<LogFilter> Filters { get; set; } = new();

    /// <summary>
    /// Gets or sets whether to enable default filters.
    /// </summary>
    public bool EnableDefaultFilters { get; set; } = true;

    /// <summary>
    /// Creates a copy of this filter configuration. The filter list is duplicated (new list)
    /// while the individual filter instances are shared, as they are treated as immutable.
    /// </summary>
    public FilterConfiguration Clone() => new()
    {
        Filters = new List<LogFilter>(Filters),
        EnableDefaultFilters = EnableDefaultFilters
    };

    /// <summary>
    /// Adds a namespace filter for Entity Framework logs.
    /// </summary>
    public FilterConfiguration AddEntityFrameworkFilter(LogLevel minimumLevel = LogLevel.Warning)
    {
        var efPatterns = new[] { "Microsoft.EntityFrameworkCore.*" };
        // Create a filter that rejects non-EF logs and low-level EF logs, but continues for valid EF logs
        Filters.Add(new NamespaceFilter(
            "EntityFramework",
            includePatterns: efPatterns,
            excludePatterns: null,
            minimumLevel: minimumLevel,
            priority: 200)
        {
            // Override behavior for integration scenarios
        });
        return this;
    }

    /// <summary>
    /// Adds a filter to exclude health check requests.
    /// </summary>
    public FilterConfiguration ExcludeHealthChecks()
    {
        Filters.Add(HttpContextFilter.ForRequestPaths("/health", "/healthz", "/ready", "/live"));
        return this;
    }

    /// <summary>
    /// Adds a filter to exclude requests from monitoring tools.
    /// </summary>
    public FilterConfiguration ExcludeMonitoringTools()
    {
        Filters.Add(HttpContextFilter.ExcludeUserAgents(
            "kube-probe", "GoogleHC", "ELB-HealthChecker", "Pingdom"));
        return this;
    }

    /// <summary>
    /// Adds a rate limiting filter to prevent log spam.
    /// </summary>
    public FilterConfiguration AddRateLimit(TimeSpan timeWindow, int maxMessages)
    {
        Filters.Add(new RateLimitFilter(
            $"RateLimit({timeWindow.TotalSeconds}s,{maxMessages})",
            timeWindow,
            maxMessages));
        return this;
    }

    /// <summary>
    /// Adds a filter for expensive operations (query time > threshold).
    /// </summary>
    public FilterConfiguration AddSlowQueryFilter(double thresholdMs)
    {
        Filters.Add(PropertyFilter.NumericCondition(
            "Duration",
            duration => duration > thresholdMs));
        return this;
    }

    /// <summary>
    /// Adds a filter for specific user actions.
    /// </summary>
    public FilterConfiguration AddUserActionFilter(params string[] actions)
    {
        foreach (var action in actions)
        {
            Filters.Add(PropertyFilter.HasValue("Action", action));
        }
        return this;
    }
}