using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging.Filtering;

/// <summary>
/// Provides the context for building filter expressions with natural syntax.
/// This class contains all available filter methods that can be used in expressions like:
/// filter => (filter.EntityFramework(LogLevel.Warning) &amp;&amp; filter.SlowQuery(1000)) || filter.BusinessCritical()
/// </summary>
public sealed class FilterExpressionContext
{
    // Cached regex patterns for performance
    private static readonly Regex HealthCheckPattern = new(@"\/health|\/healthcheck|\/ping", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MonitoringPattern = new(@"\/metrics|\/status|\/monitoring|\/diagnostics", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Compiled wildcard regexes are cached by pattern: building one is costly and the same
    // patterns recur across filter evaluations. Bounded in practice by the number of distinct patterns.
    private static readonly ConcurrentDictionary<string, Regex> WildcardRegexCache = new(StringComparer.Ordinal);

    #region Log Level Filters

    /// <summary>
    /// Filters by minimum log level.
    /// </summary>
    /// <param name="minimumLevel">The minimum log level to include</param>
    /// <returns>FilterResult that passes logs at or above the specified level</returns>
    public FilterResult Level(LogLevel minimumLevel)
    {
        return new FilterResult((entry, context) => entry.LogLevel >= minimumLevel);
    }

    /// <summary>
    /// Filters by exact log level.
    /// </summary>
    /// <param name="level">The exact log level to match</param>
    /// <returns>FilterResult that passes only logs at the specified level</returns>
    public FilterResult ExactLevel(LogLevel level)
    {
        return new FilterResult((entry, context) => entry.LogLevel == level);
    }

    /// <summary>
    /// Filters for debug level logs only.
    /// </summary>
    /// <returns>FilterResult that passes only Debug level logs</returns>
    public FilterResult Debug() => ExactLevel(LogLevel.Debug);

    /// <summary>
    /// Filters for information level logs only.
    /// </summary>
    /// <returns>FilterResult that passes only Information level logs</returns>
    public FilterResult Information() => ExactLevel(LogLevel.Information);

    /// <summary>
    /// Filters for warning level logs only.
    /// </summary>
    /// <returns>FilterResult that passes only Warning level logs</returns>
    public FilterResult Warning() => ExactLevel(LogLevel.Warning);

    /// <summary>
    /// Filters for error level logs only.
    /// </summary>
    /// <returns>FilterResult that passes only Error level logs</returns>
    public FilterResult Error() => ExactLevel(LogLevel.Error);

    /// <summary>
    /// Filters for critical level logs only.
    /// </summary>
    /// <returns>FilterResult that passes only Critical level logs</returns>
    public FilterResult Critical() => ExactLevel(LogLevel.Critical);

    #endregion

    #region Category Filters

    /// <summary>
    /// Filters by category name pattern using wildcards (* and ?).
    /// </summary>
    /// <param name="pattern">Pattern to match (supports * and ? wildcards)</param>
    /// <returns>FilterResult that passes logs matching the category pattern</returns>
    public FilterResult Category(string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return FilterResult.True;

        var regex = CreateWildcardRegex(pattern);
        return new FilterResult((entry, context) => regex.IsMatch(entry.Category));
    }

    /// <summary>
    /// Filters to include only Entity Framework logs.
    /// </summary>
    /// <param name="minimumLevel">Optional minimum level for EF logs</param>
    /// <returns>FilterResult that passes Entity Framework logs</returns>
    public FilterResult EntityFramework(LogLevel minimumLevel = LogLevel.Information)
    {
        return new FilterResult((entry, context) =>
            (entry.Category.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) ||
             entry.Category.StartsWith("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)) &&
            entry.LogLevel >= minimumLevel);
    }

    /// <summary>
    /// Filters to exclude Microsoft framework logs.
    /// </summary>
    /// <returns>FilterResult that excludes Microsoft.* categories</returns>
    public FilterResult NotMicrosoft()
    {
        return new FilterResult((entry, context) =>
            !entry.Category.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Filters to include only application logs (non-framework).
    /// </summary>
    /// <param name="applicationNamespace">Optional application namespace to include</param>
    /// <returns>FilterResult that passes application logs</returns>
    public FilterResult Application(string? applicationNamespace = null)
    {
        return new FilterResult((entry, context) =>
        {
            var isNotFramework = !entry.Category.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) &&
                                !entry.Category.StartsWith("System.", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(applicationNamespace))
                return isNotFramework;

            return isNotFramework && entry.Category.StartsWith(applicationNamespace, StringComparison.OrdinalIgnoreCase);
        });
    }

    #endregion

    #region Property Filters

    /// <summary>
    /// Filters logs that have a specific property with any value.
    /// </summary>
    /// <param name="propertyName">Name of the property to check</param>
    /// <returns>FilterResult that passes logs containing the specified property</returns>
    public FilterResult HasProperty(string propertyName)
    {
        return new FilterResult((entry, context) => entry.Properties.ContainsKey(propertyName));
    }

    /// <summary>
    /// Filters logs that have a specific property with a specific value.
    /// </summary>
    /// <param name="propertyName">Name of the property to check</param>
    /// <param name="expectedValue">Expected value of the property</param>
    /// <returns>FilterResult that passes logs with matching property value</returns>
    public FilterResult HasProperty(string propertyName, object expectedValue)
    {
        return new FilterResult((entry, context) =>
            entry.Properties.TryGetValue(propertyName, out var value) &&
            Equals(value, expectedValue));
    }

    /// <summary>
    /// Filters logs that have structured properties (not just simple key-value pairs).
    /// </summary>
    /// <returns>FilterResult that passes logs with structured data</returns>
    public FilterResult HasStructuredProperties()
    {
        // > 1 because a single marker flag (e.g. ["BusinessEvent"] = true) is not "structured data"
        return new FilterResult((entry, context) => entry.Properties.Count > 1);
    }

    /// <summary>
    /// Filters logs that contain structured data suitable for analytics.
    /// </summary>
    /// <returns>FilterResult that passes logs with structured data</returns>
    public FilterResult HasStructuredData() => HasStructuredProperties();

    #endregion

    #region Performance Filters

    /// <summary>
    /// Filters for slow database queries based on duration.
    /// </summary>
    /// <param name="minimumMilliseconds">Minimum duration in milliseconds</param>
    /// <returns>FilterResult that passes slow queries</returns>
    public FilterResult SlowQuery(double minimumMilliseconds)
    {
        return new FilterResult((entry, context) =>
        {
            if (!entry.Properties.TryGetValue("Duration", out var durationObj))
                return false;

            return durationObj switch
            {
                double duration => duration >= minimumMilliseconds,
                float durationFloat => durationFloat >= minimumMilliseconds,
                int durationInt => durationInt >= minimumMilliseconds,
                long durationLong => durationLong >= minimumMilliseconds,
                decimal durationDecimal => (double)durationDecimal >= minimumMilliseconds,
                string durationStr when double.TryParse(durationStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed >= minimumMilliseconds,
                _ => false
            };
        });
    }

    /// <summary>
    /// Filters for operations that consume high memory.
    /// </summary>
    /// <param name="minimumMegabytes">Minimum memory usage in megabytes</param>
    /// <returns>FilterResult that passes high memory operations</returns>
    public FilterResult HighMemory(double minimumMegabytes)
    {
        return new FilterResult((entry, context) =>
        {
            var minimumBytes = minimumMegabytes * 1024 * 1024;

            if (entry.Properties.TryGetValue("MemoryAllocated", out var memoryObj) ||
                entry.Properties.TryGetValue("Memory", out memoryObj) ||
                entry.Properties.TryGetValue("Bytes", out memoryObj))
            {
                return memoryObj switch
                {
                    long memory => memory >= minimumBytes,
                    int memoryInt => memoryInt >= minimumBytes,
                    double memoryDouble => memoryDouble >= minimumBytes,
                    string memoryStr when double.TryParse(memoryStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed >= minimumBytes,
                    _ => false
                };
            }

            return false;
        });
    }

    #endregion

    #region HTTP Context Filters

    /// <summary>
    /// Filters to exclude health check requests.
    /// </summary>
    /// <returns>FilterResult that excludes health check logs</returns>
    public FilterResult HealthCheck()
    {
        return new FilterResult((entry, context) =>
        {
            var path = context.RequestPath
                ?? (entry.Properties.TryGetValue("RequestPath", out var v) ? v?.ToString() : null);
            if (path != null)
                return HealthCheckPattern.IsMatch(path);

            return entry.Category.Contains("Health", StringComparison.OrdinalIgnoreCase) ||
                   entry.Message.Contains("health", StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// Filters to exclude monitoring and metrics requests.
    /// </summary>
    /// <returns>FilterResult that excludes monitoring logs</returns>
    public FilterResult MonitoringTools()
    {
        return new FilterResult((entry, context) =>
        {
            if (context.RequestPath != null)
                return MonitoringPattern.IsMatch(context.RequestPath);

            return entry.Category.Contains("Metrics", StringComparison.OrdinalIgnoreCase) ||
                   entry.Category.Contains("Monitoring", StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// Filters by HTTP request path pattern.
    /// </summary>
    /// <param name="pathPattern">Path pattern to match (supports wildcards)</param>
    /// <returns>FilterResult that passes logs from matching request paths</returns>
    public FilterResult RequestPath(string pathPattern)
    {
        var regex = CreateWildcardRegex(pathPattern);
        return new FilterResult((entry, context) =>
        {
            var path = context.RequestPath
                ?? (entry.Properties.TryGetValue("RequestPath", out var v) ? v?.ToString() : null);
            return path != null && regex.IsMatch(path);
        });
    }

    /// <summary>
    /// Filters by HTTP method.
    /// </summary>
    /// <param name="method">HTTP method to match (GET, POST, etc.)</param>
    /// <returns>FilterResult that passes logs from the specified HTTP method</returns>
    public FilterResult HttpMethod(string method)
    {
        return new FilterResult((entry, context) =>
            string.Equals(context.HttpMethod, method, StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Business Context Filters

    /// <summary>
    /// Filters for business-critical events.
    /// </summary>
    /// <returns>FilterResult that passes business-critical logs</returns>
    public FilterResult BusinessCritical()
    {
        return new FilterResult((entry, context) =>
            entry.Properties.ContainsKey("BusinessCritical") ||
            entry.Properties.ContainsKey("EventType") &&
            entry.Properties["EventType"]?.ToString()?.Contains("Critical", StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>
    /// Filters for user actions and interactions.
    /// </summary>
    /// <returns>FilterResult that passes user action logs</returns>
    public FilterResult UserAction()
    {
        return new FilterResult((entry, context) =>
            entry.Properties.ContainsKey("UserAction") ||
            entry.Properties.ContainsKey("Action") ||
            entry.Category.Contains("Controller", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Filters for specific user actions.
    /// </summary>
    /// <param name="actions">Specific actions to match</param>
    /// <returns>FilterResult that passes logs for the specified actions</returns>
    public FilterResult UserAction(params string[] actions)
    {
        return new FilterResult((entry, context) =>
        {
            if (entry.Properties.TryGetValue("Action", out var actionObj) ||
                entry.Properties.TryGetValue("UserAction", out actionObj))
            {
                var actionStr = actionObj?.ToString();
                return actions.Any(a => string.Equals(actionStr, a, StringComparison.OrdinalIgnoreCase));
            }
            return false;
        });
    }

    /// <summary>
    /// Filters for business events.
    /// </summary>
    /// <returns>FilterResult that passes business event logs</returns>
    public FilterResult BusinessEvent()
    {
        return new FilterResult((entry, context) =>
            entry.Properties.ContainsKey("BusinessEvent") ||
            entry.Properties.ContainsKey("BusinessEventType") ||
            entry.Properties.ContainsKey("EventType"));
    }

    /// <summary>
    /// Filters for payment-related events.
    /// </summary>
    /// <returns>FilterResult that passes payment event logs</returns>
    public FilterResult PaymentEvent()
    {
        return new FilterResult((entry, context) =>
            entry.Category.Contains("Payment", StringComparison.OrdinalIgnoreCase) ||
            entry.Properties.ContainsKey("PaymentId") ||
            entry.Properties.ContainsKey("TransactionId") ||
            entry.Message.Contains("payment", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Filters for security-related events.
    /// </summary>
    /// <returns>FilterResult that passes security event logs</returns>
    public FilterResult SecurityEvent()
    {
        return new FilterResult((entry, context) =>
            entry.Category.Contains("Security", StringComparison.OrdinalIgnoreCase) ||
            entry.Properties.ContainsKey("SecurityEvent") ||
            entry.Message.Contains("security", StringComparison.OrdinalIgnoreCase) ||
            entry.Message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
            entry.Message.Contains("forbidden", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Filters for security violations.
    /// </summary>
    /// <returns>FilterResult that passes security violation logs</returns>
    public FilterResult SecurityViolation()
    {
        return new FilterResult((entry, context) =>
            entry.Properties.ContainsKey("SecurityViolation") ||
            entry.Properties.ContainsKey("Action") &&
            entry.Properties["Action"]?.ToString()?.Equals("SecurityViolation", StringComparison.OrdinalIgnoreCase) == true);
    }

    #endregion

    #region Environment and Configuration Filters

    /// <summary>
    /// Filters by environment name.
    /// </summary>
    /// <param name="environmentName">Environment name to match (Development, Production, etc.)</param>
    /// <returns>FilterResult that passes logs from the specified environment</returns>
    public FilterResult Environment(string environmentName)
    {
        return new FilterResult((entry, context) =>
            string.Equals(context.Environment, environmentName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), environmentName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Filters for development environment only.
    /// </summary>
    /// <returns>FilterResult that passes logs in development environment</returns>
    public FilterResult Development() => Environment("Development");

    /// <summary>
    /// Filters for production environment only.
    /// </summary>
    /// <returns>FilterResult that passes logs in production environment</returns>
    public FilterResult Production() => Environment("Production");

    /// <summary>
    /// Filters by feature flag status.
    /// </summary>
    /// <param name="featureName">Feature flag name</param>
    /// <param name="enabled">Whether the feature should be enabled</param>
    /// <returns>FilterResult that passes logs when feature flag matches</returns>
    public FilterResult FeatureFlag(string featureName, bool enabled = true)
    {
        return new FilterResult((entry, context) =>
        {
            if (entry.Properties.TryGetValue($"Feature_{featureName}", out var value) ||
                entry.Properties.TryGetValue("FeatureFlag", out value))
            {
                return value switch
                {
                    bool boolValue => boolValue == enabled,
                    string strValue => bool.TryParse(strValue, out var parsed) && parsed == enabled,
                    _ => false
                };
            }
            return false;
        });
    }

    #endregion

    #region User Context Filters

    /// <summary>
    /// Filters by user segment (for A/B testing, analytics).
    /// </summary>
    /// <param name="segment">User segment to match</param>
    /// <returns>FilterResult that passes logs from the specified user segment</returns>
    public FilterResult UserSegment(string segment)
    {
        return new FilterResult((entry, context) =>
            entry.Properties.TryGetValue("UserSegment", out var value) &&
            string.Equals(value?.ToString(), segment, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Filters by user role.
    /// </summary>
    /// <param name="role">User role to match</param>
    /// <returns>FilterResult that passes logs from users with the specified role</returns>
    public FilterResult UserRole(string role)
    {
        return new FilterResult((entry, context) =>
            entry.Properties.TryGetValue("UserRole", out var value) &&
            string.Equals(value?.ToString(), role, StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Rate Limiting

    /// <summary>
    /// Applies high-performance rate limiting to prevent log spam using token bucket algorithm.
    /// Recommended for most scenarios as it allows bursts while maintaining average rate.
    /// </summary>
    /// <param name="maxMessages">Maximum number of messages</param>
    /// <param name="timeWindow">Time window for rate limiting</param>
    /// <returns>FilterResult that applies optimized rate limiting</returns>
    public FilterResult RateLimit(int maxMessages, TimeSpan timeWindow)
    {
        return HighPerformanceRateLimiter.CreateRateLimitedFilter(
            HighPerformanceRateLimiter.RateLimitStrategy.TokenBucket, maxMessages, timeWindow);
    }

    /// <summary>
    /// Applies rate limiting with a simpler time window specification.
    /// </summary>
    /// <param name="timeWindow">Time window for rate limiting</param>
    /// <param name="maxMessages">Maximum number of messages in the time window</param>
    /// <returns>FilterResult that applies optimized rate limiting</returns>
    public FilterResult RateLimit(TimeSpan timeWindow, int maxMessages) => RateLimit(maxMessages, timeWindow);

    /// <summary>
    /// Applies rate limiting with a specific strategy for advanced scenarios.
    /// </summary>
    /// <param name="strategy">Rate limiting strategy</param>
    /// <param name="maxMessages">Maximum number of messages</param>
    /// <param name="timeWindow">Time window for rate limiting</param>
    /// <returns>FilterResult with specified rate limiting strategy</returns>
    public FilterResult RateLimit(HighPerformanceRateLimiter.RateLimitStrategy strategy, int maxMessages, TimeSpan timeWindow)
    {
        return HighPerformanceRateLimiter.CreateRateLimitedFilter(strategy, maxMessages, timeWindow);
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Creates a group for complex logical operations.
    /// </summary>
    /// <param name="expression">Expression to group</param>
    /// <returns>The same FilterResult (grouping is implicit in C# operator precedence)</returns>
    public FilterResult Group(Func<FilterExpressionContext, FilterResult> expression)
    {
        return expression(this);
    }

    /// <summary>
    /// Returns a compiled wildcard regex for the given pattern, caching by pattern so the
    /// (relatively expensive) compilation happens once per distinct pattern rather than on
    /// every filter invocation. Thread-safe via <see cref="ConcurrentDictionary{TKey,TValue}"/>.
    /// </summary>
    /// <param name="pattern">Pattern with * and ? wildcards</param>
    /// <returns>Compiled regex for the pattern</returns>
    private static Regex CreateWildcardRegex(string pattern)
        => WildcardRegexCache.GetOrAdd(pattern, static p =>
        {
            var regexPattern = "^" + Regex.Escape(p)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            return new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        });

    #endregion
}

/// <summary>
/// Simple rate limiter implementation for filter expressions.
/// Thread-safe and efficient for high-throughput scenarios.
/// </summary>
internal sealed class SimpleRateLimiter(int maxMessages, TimeSpan timeWindow)
{
    private readonly Queue<DateTime> _timestamps = new();
    private readonly object _lock = new();

    public bool ShouldAllow()
    {
        var now = DateTime.UtcNow;

        lock (_lock)
        {
            // Remove old timestamps outside the time window
            while (_timestamps.Count > 0 && now - _timestamps.Peek() > timeWindow)
            {
                _timestamps.Dequeue();
            }

            // Check if we're under the limit
            if (_timestamps.Count < maxMessages)
            {
                _timestamps.Enqueue(now);
                return true;
            }

            return false;
        }
    }
}