using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Filtering;

/// <summary>
/// Filter based on namespace patterns.
/// Filters log entries based on the logger category name (typically the class namespace).
/// </summary>
public sealed class NamespaceFilter : LogFilter
{
    private readonly string[] _includePatterns;
    private readonly string[] _excludePatterns;
    private readonly LogLevel _minimumLevel;

    public NamespaceFilter(
        string name,
        string[]? includePatterns = null,
        string[]? excludePatterns = null,
        LogLevel minimumLevel = LogLevel.Trace,
        int priority = 100)
    {
        Name = name;
        Priority = priority;
        _includePatterns = includePatterns ?? Array.Empty<string>();
        _excludePatterns = excludePatterns ?? Array.Empty<string>();
        _minimumLevel = minimumLevel;
    }

    public override LogFilterResult ShouldLog(LogEntry logEntry, LogFilterContext context)
    {
        // Check log level first
        if (logEntry.LogLevel < _minimumLevel)
        {
            return LogFilterResult.Reject;
        }

        var category = logEntry.Category;

        // Check exclude patterns first
        foreach (var pattern in _excludePatterns)
        {
            if (MatchesPattern(category, pattern))
            {
                return LogFilterResult.Reject;
            }
        }

        // If we have include patterns, check them
        if (_includePatterns.Length > 0)
        {
            foreach (var pattern in _includePatterns)
            {
                if (MatchesPattern(category, pattern))
                {
                    return LogFilterResult.Allow;
                }
            }
            // If we have include patterns but none matched, reject
            return LogFilterResult.Reject;
        }

        // No include patterns, and not excluded, so continue
        return LogFilterResult.Continue;
    }

    private static bool MatchesPattern(string category, string pattern)
    {
        // Support wildcards
        if (pattern.Contains('*'))
        {
            var regex = "^" + pattern.Replace("*", ".*") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(category, regex,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        // Exact match or prefix match
        return category.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
               category.StartsWith(pattern + ".", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Filter based on HTTP context properties (ASP.NET Core).
/// </summary>
public sealed class HttpContextFilter : LogFilter
{
    private readonly Func<object?, LogEntry, bool> _predicate;

    public HttpContextFilter(
        string name,
        Func<object?, LogEntry, bool> predicate,
        int priority = 200)
    {
        Name = name;
        Priority = priority;
        _predicate = predicate;
    }

    public override LogFilterResult ShouldLog(LogEntry logEntry, LogFilterContext context)
    {
        if (context.HttpContext == null)
        {
            return LogFilterResult.Continue;
        }

        return _predicate(context.HttpContext, logEntry)
            ? LogFilterResult.Continue
            : LogFilterResult.Reject;
    }

    /// <summary>
    /// Creates a filter that only logs for specific HTTP methods.
    /// </summary>
    public static HttpContextFilter ForHttpMethods(params string[] methods)
    {
        var methodSet = new HashSet<string>(methods, StringComparer.OrdinalIgnoreCase);

        return new HttpContextFilter(
            $"HttpMethod({string.Join(",", methods)})",
            (httpContext, _) =>
            {
                // This would need proper HttpContext access in real implementation
                var method = GetHttpMethod(httpContext);
                return method != null && methodSet.Contains(method);
            });
    }

    /// <summary>
    /// Creates a filter that only logs for specific request paths.
    /// </summary>
    public static HttpContextFilter ForRequestPaths(params string[] pathPatterns)
    {
        return new HttpContextFilter(
            $"RequestPath({string.Join(",", pathPatterns)})",
            (httpContext, _) =>
            {
                var path = GetRequestPath(httpContext);
                if (path == null)
                    return false;

                foreach (var pattern in pathPatterns)
                {
                    if (path.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            });
    }

    /// <summary>
    /// Creates a filter that excludes requests from specific user agents.
    /// </summary>
    public static HttpContextFilter ExcludeUserAgents(params string[] userAgentPatterns)
    {
        return new HttpContextFilter(
            $"ExcludeUserAgent({string.Join(",", userAgentPatterns)})",
            (httpContext, _) =>
            {
                var userAgent = GetUserAgent(httpContext);
                if (userAgent == null)
                    return true;

                foreach (var pattern in userAgentPatterns)
                {
                    if (userAgent.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                return true;
            });
    }

    private static string? GetHttpMethod(object? httpContext)
        => httpContext is Microsoft.AspNetCore.Http.HttpContext ctx ? ctx.Request.Method : null;

    private static string? GetRequestPath(object? httpContext)
        => httpContext is Microsoft.AspNetCore.Http.HttpContext ctx ? ctx.Request.Path.Value : null;

    private static string? GetUserAgent(object? httpContext)
    {
        if (httpContext is not Microsoft.AspNetCore.Http.HttpContext ctx)
            return null;

        return ctx.Request.Headers.TryGetValue("User-Agent", out var userAgent)
            ? userAgent.ToString()
            : null;
    }
}

/// <summary>
/// Filter based on custom properties in log entries.
/// </summary>
public sealed class PropertyFilter : LogFilter
{
    private readonly string _propertyName;
    private readonly Func<object?, bool> _predicate;

    public PropertyFilter(
        string name,
        string propertyName,
        Func<object?, bool> predicate,
        int priority = 150)
    {
        Name = name;
        Priority = priority;
        _propertyName = propertyName;
        _predicate = predicate;
    }

    public override LogFilterResult ShouldLog(LogEntry logEntry, LogFilterContext context)
    {
        if (!logEntry.Properties.TryGetValue(_propertyName, out var value))
        {
            return LogFilterResult.Continue;
        }

        return _predicate(value) ? LogFilterResult.Continue : LogFilterResult.Reject;
    }

    /// <summary>
    /// Creates a filter that only logs entries with a specific property value.
    /// </summary>
    public static PropertyFilter HasValue(string propertyName, object expectedValue)
    {
        return new PropertyFilter(
            $"Property[{propertyName}]={expectedValue}",
            propertyName,
            value => Equals(value, expectedValue));
    }

    /// <summary>
    /// Creates a filter that only logs entries where a numeric property meets a condition.
    /// </summary>
    public static PropertyFilter NumericCondition(string propertyName, Func<double, bool> condition)
    {
        return new PropertyFilter(
            $"Property[{propertyName}]=NumericCondition",
            propertyName,
            value =>
            {
                if (value == null)
                    return false;

                // Handle numeric types directly
                double numValue = value switch
                {
                    double d => d,
                    float f => f,
                    decimal m => (double)m,
                    int i => i,
                    long l => l,
                    short s => s,
                    byte b => b,
                    _ => double.TryParse(value.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : double.NaN
                };

                if (double.IsNaN(numValue))
                    return false;
                return condition(numValue);
            });
    }
}

/// <summary>
/// Filter that applies rate limiting to prevent log spam.
/// </summary>
public sealed class RateLimitFilter : LogFilter
{
    private readonly TimeSpan _timeWindow;
    private readonly int _maxMessages;
    private readonly Dictionary<string, MessageWindow> _windows = new();
    private readonly object _lock = new();
    // Reused removal buffer for pruning; only touched under _lock.
    private List<string>? _pruneBuffer;

    public RateLimitFilter(
        string name,
        TimeSpan timeWindow,
        int maxMessages,
        int priority = 50)
    {
        Name = name;
        Priority = priority;
        _timeWindow = timeWindow;
        _maxMessages = maxMessages;
    }

    public override LogFilterResult ShouldLog(LogEntry logEntry, LogFilterContext context)
    {
        var key = $"{logEntry.Category}:{logEntry.LogLevel}";
        var now = DateTime.UtcNow;

        lock (_lock)
        {
            if (!_windows.TryGetValue(key, out var window))
            {
                window = new MessageWindow(now);
                _windows[key] = window;
            }

            // Clean old windows periodically. Single-pass, no LINQ allocations:
            // collect expired keys into a reused buffer, then remove. The buffer is
            // only grown when pruning actually runs (rare: only past the threshold).
            if (_windows.Count > 1000)
            {
                var cutoff = now - _timeWindow;
                _pruneBuffer ??= new List<string>();
                _pruneBuffer.Clear();

                foreach (var kvp in _windows)
                {
                    if (kvp.Value.WindowStart < cutoff)
                    {
                        _pruneBuffer.Add(kvp.Key);
                    }
                }

                for (var i = 0; i < _pruneBuffer.Count; i++)
                {
                    _windows.Remove(_pruneBuffer[i]);
                }
            }

            // Check if we're in a new time window
            if (now - window.WindowStart > _timeWindow)
            {
                window.WindowStart = now;
                window.MessageCount = 0;
            }

            if (window.MessageCount >= _maxMessages)
            {
                return LogFilterResult.Reject;
            }

            window.MessageCount++;
            return LogFilterResult.Continue;
        }
    }

    private sealed class MessageWindow(DateTime windowStart)
    {
        public DateTime WindowStart { get; set; } = windowStart;
        public int MessageCount { get; set; } = 0;
    }
}