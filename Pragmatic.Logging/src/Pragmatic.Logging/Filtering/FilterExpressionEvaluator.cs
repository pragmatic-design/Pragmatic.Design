using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Filtering;

/// <summary>
/// High-performance evaluator for filter expressions with compilation caching and optimization.
/// </summary>
public sealed class FilterExpressionEvaluator
{
    // Cache compiled expressions for performance - thread-safe
    // Key is a stable string to avoid int hash collisions causing incorrect cache hits
    private static readonly ConcurrentDictionary<string, CompiledFilterExpression> CompiledCache = new();

    // Metrics for performance monitoring
    private static long _totalEvaluations;
    private static long _cacheHits;
    private static long _cacheMisses;

    /// <summary>
    /// Evaluates a filter expression against a log entry with maximum performance.
    /// </summary>
    /// <param name="expression">The filter expression to evaluate</param>
    /// <param name="logEntry">The log entry to test</param>
    /// <param name="context">The filter context</param>
    /// <returns>True if the log entry passes the filter</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Evaluate(FilterExpression expression, LogEntry logEntry, LogFilterContext context)
    {
        Interlocked.Increment(ref _totalEvaluations);

        // Get the expression hash for caching
        var expressionHash = GetExpressionHash(expression);

        // Try to get compiled expression from cache
        if (CompiledCache.TryGetValue(expressionHash, out var compiled))
        {
            Interlocked.Increment(ref _cacheHits);
            return compiled.Evaluate(logEntry, context);
        }

        // Compile and cache the expression
        Interlocked.Increment(ref _cacheMisses);
        var newCompiled = CompileExpression(expression, expressionHash);
        CompiledCache.TryAdd(expressionHash, newCompiled);

        return newCompiled.Evaluate(logEntry, context);
    }

    /// <summary>
    /// Evaluates a filter expression with detailed performance metrics.
    /// </summary>
    /// <param name="expression">The filter expression to evaluate</param>
    /// <param name="logEntry">The log entry to test</param>
    /// <param name="context">The filter context</param>
    /// <returns>Evaluation result with performance data</returns>
    public static FilterEvaluationResult EvaluateWithMetrics(FilterExpression expression, LogEntry logEntry, LogFilterContext context)
    {
        var expressionHash = GetExpressionHash(expression);
        // Check cache state BEFORE evaluation so CacheHit reflects a true hit, not a just-compiled entry
        var wasCached = CompiledCache.ContainsKey(expressionHash);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = Evaluate(expression, logEntry, context);
            stopwatch.Stop();

            return new FilterEvaluationResult
            {
                Passed = result,
                EvaluationTime = stopwatch.Elapsed,
                CacheHit = wasCached
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            return new FilterEvaluationResult
            {
                Passed = false,
                EvaluationTime = stopwatch.Elapsed,
                Exception = ex
            };
        }
    }

    /// <summary>
    /// Pre-compiles a filter expression for optimal performance.
    /// Useful for expressions that will be evaluated many times.
    /// </summary>
    /// <param name="expression">The filter expression to compile</param>
    /// <returns>Compiled expression ready for high-performance evaluation</returns>
    public static CompiledFilterExpression Compile(FilterExpression expression)
    {
        var expressionHash = GetExpressionHash(expression);

        if (CompiledCache.TryGetValue(expressionHash, out var cached))
            return cached;

        var compiled = CompileExpression(expression, expressionHash);
        CompiledCache.TryAdd(expressionHash, compiled);

        return compiled;
    }

    /// <summary>
    /// Clears the expression compilation cache.
    /// Useful for testing or memory management.
    /// </summary>
    public static void ClearCache()
    {
        CompiledCache.Clear();
    }

    /// <summary>
    /// Gets performance statistics for the expression evaluator.
    /// </summary>
    /// <returns>Performance statistics</returns>
    public static FilterEvaluatorStatistics GetStatistics()
    {
        var totalEvals = Interlocked.Read(ref _totalEvaluations);
        var hits = Interlocked.Read(ref _cacheHits);
        var misses = Interlocked.Read(ref _cacheMisses);

        return new FilterEvaluatorStatistics
        {
            TotalEvaluations = totalEvals,
            CacheHits = hits,
            CacheMisses = misses,
            CacheHitRatio = totalEvals > 0 ? (double)hits / totalEvals : 0.0,
            CachedExpressions = CompiledCache.Count
        };
    }

    /// <summary>
    /// Resets performance statistics.
    /// </summary>
    public static void ResetStatistics()
    {
        Interlocked.Exchange(ref _totalEvaluations, 0);
        Interlocked.Exchange(ref _cacheHits, 0);
        Interlocked.Exchange(ref _cacheMisses, 0);
    }

    /// <summary>
    /// Compiles a filter expression into an optimized evaluator.
    /// </summary>
    /// <param name="expression">The expression to compile</param>
    /// <param name="key">The stable string key used for cache lookup</param>
    /// <returns>Compiled expression</returns>
    private static CompiledFilterExpression CompileExpression(FilterExpression expression, string key)
    {
        try
        {
            // Use the FilterExpressionCompiler to create properly compiled expressions
            return FilterExpressionCompiler.Compile(expression);
        }
        catch (Exception ex)
        {
            // Return a failed compilation result using stable key hash for internal tracking
            return new CompiledFilterExpression(ex, key.GetHashCode());
        }
    }

    /// <summary>
    /// Generates a stable string key for an expression for collision-free caching.
    /// </summary>
    /// <param name="expression">The expression to key</param>
    /// <returns>Stable string key for the expression</returns>
    private static string GetExpressionHash(FilterExpression expression)
    {
        // Use the method's metadata for a stable, collision-free key
        var method = expression.Method;
        var target = expression.Target;
        var targetHash = target?.GetHashCode() ?? 0;

        return $"{method.DeclaringType?.FullName}|{method.Name}|{method}|{targetHash}";
    }
}


/// <summary>
/// Represents the result of evaluating a filter expression with performance metrics.
/// </summary>
public sealed class FilterEvaluationResult
{
    /// <summary>
    /// Gets or sets whether the log entry passed the filter.
    /// </summary>
    public bool Passed { get; set; }

    /// <summary>
    /// Gets or sets the time taken to evaluate the expression.
    /// </summary>
    public TimeSpan EvaluationTime { get; set; }

    /// <summary>
    /// Gets or sets whether the evaluation used a cached compiled expression.
    /// </summary>
    public bool CacheHit { get; set; }

    /// <summary>
    /// Gets or sets any exception that occurred during evaluation.
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// Gets whether the evaluation was successful (no exception).
    /// </summary>
    public bool IsSuccess => Exception == null;

    /// <summary>
    /// Returns a string representation for debugging.
    /// </summary>
    /// <returns>String representation</returns>
    public override string ToString()
    {
        var status = IsSuccess ? (Passed ? "Passed" : "Failed") : $"Error: {Exception?.Message}";
        var cacheStatus = CacheHit ? "Cache Hit" : "Cache Miss";
        return $"FilterEvaluationResult({status}, {EvaluationTime.TotalMicroseconds:F1}μs, {cacheStatus})";
    }
}

/// <summary>
/// Performance statistics for the filter expression evaluator.
/// </summary>
public sealed class FilterEvaluatorStatistics
{
    /// <summary>
    /// Gets or sets the total number of expression evaluations performed.
    /// </summary>
    public long TotalEvaluations { get; set; }

    /// <summary>
    /// Gets or sets the number of cache hits.
    /// </summary>
    public long CacheHits { get; set; }

    /// <summary>
    /// Gets or sets the number of cache misses.
    /// </summary>
    public long CacheMisses { get; set; }

    /// <summary>
    /// Gets or sets the cache hit ratio (0.0 to 1.0).
    /// </summary>
    public double CacheHitRatio { get; set; }

    /// <summary>
    /// Gets or sets the number of expressions currently cached.
    /// </summary>
    public int CachedExpressions { get; set; }

    /// <summary>
    /// Returns a string representation for debugging.
    /// </summary>
    /// <returns>String representation</returns>
    public override string ToString()
    {
        return $"FilterEvaluatorStatistics(Evaluations: {TotalEvaluations}, " +
               $"Cache Hit Ratio: {CacheHitRatio:P1}, Cached: {CachedExpressions})";
    }
}

/// <summary>
/// Delegate type for filter expressions.
/// </summary>
/// <param name="context">The filter expression context providing all available filter methods</param>
/// <returns>FilterResult representing the filter logic</returns>
public delegate FilterResult FilterExpression(FilterExpressionContext context);