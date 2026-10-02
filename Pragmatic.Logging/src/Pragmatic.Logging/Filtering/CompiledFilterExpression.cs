using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Filtering;

/// <summary>
/// High-performance compiled filter expression that generates optimized delegates for ultra-fast evaluation.
/// Uses expression trees to compile filter logic into native code for maximum performance.
/// </summary>
public sealed class CompiledFilterExpression
{
    // Ultra-fast evaluation delegate - compiled to native code
    private readonly Func<LogEntry, LogFilterContext, bool>? _compiledEvaluator;
    private readonly Exception? _compilationException;
    private readonly int _hash;

    // Unused reflection caches removed — all member access is now AOT-safe.

    /// <summary>
    /// Creates a successfully compiled filter expression with native delegate.
    /// </summary>
    /// <param name="compiledEvaluator">The compiled evaluation delegate</param>
    /// <param name="hash">The expression hash</param>
    internal CompiledFilterExpression(Func<LogEntry, LogFilterContext, bool> compiledEvaluator, int hash)
    {
        _compiledEvaluator = compiledEvaluator;
        _hash = hash;
    }

    /// <summary>
    /// Creates a failed compilation result.
    /// </summary>
    /// <param name="exception">The compilation exception</param>
    /// <param name="hash">The expression hash</param>
    internal CompiledFilterExpression(Exception exception, int hash)
    {
        _compilationException = exception;
        _hash = hash;
    }

    /// <summary>
    /// Evaluates the compiled expression with maximum performance (typically less than 10ns).
    /// </summary>
    /// <param name="logEntry">The log entry to evaluate</param>
    /// <param name="context">The filter context</param>
    /// <returns>True if the log entry passes the filter</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Evaluate(LogEntry logEntry, LogFilterContext context)
    {
        // If compilation failed, return false (reject the log)
        if (_compilationException != null)
            return false;

        // Ultra-fast evaluation via compiled delegate - typically <10ns
        return _compiledEvaluator!(logEntry, context);
    }

    /// <summary>
    /// Gets the compilation exception if the expression failed to compile.
    /// </summary>
    public Exception? CompilationException => _compilationException;

    /// <summary>
    /// Gets whether the expression compiled successfully.
    /// </summary>
    public bool IsValid => _compilationException == null && _compiledEvaluator != null;

    /// <summary>
    /// Gets the hash of the original expression.
    /// </summary>
    public int Hash => _hash;

    /// <summary>
    /// Returns a string representation for debugging.
    /// </summary>
    public override string ToString()
    {
        if (_compilationException != null)
            return $"CompiledFilterExpression(Failed: {_compilationException.Message})";

        return $"CompiledFilterExpression(Hash: {_hash}, Compiled: {_compiledEvaluator != null})";
    }
}

/// <summary>
/// Advanced expression compiler that generates ultra-fast evaluation delegates.
/// Uses expression trees and caching for maximum performance.
/// </summary>
public static class FilterExpressionCompiler
{
    // Cache for compiled expressions to avoid recompilation
    // Key is int to avoid per-call string allocation
    private static readonly ConcurrentDictionary<int, CompiledFilterExpression> CompilationCache = new();

    /// <summary>
    /// Compiles a filter expression into an ultra-fast evaluation delegate.
    /// </summary>
    /// <param name="expression">The filter expression to compile</param>
    /// <returns>Compiled expression optimized for performance</returns>
    public static CompiledFilterExpression Compile(FilterExpression expression)
    {
        var hash = GetExpressionHash(expression);

        // Check cache first
        if (CompilationCache.TryGetValue(hash, out var cached))
            return cached;

        try
        {
            // Generate optimized evaluation delegate
            var compiledDelegate = CompileToDelegate(expression);
            var compiled = new CompiledFilterExpression(compiledDelegate, hash);

            // Cache the result
            CompilationCache.TryAdd(hash, compiled);
            return compiled;
        }
        catch (Exception ex)
        {
            var failed = new CompiledFilterExpression(ex, hash);
            CompilationCache.TryAdd(hash, failed);
            return failed;
        }
    }

    /// <summary>
    /// Builds an evaluation delegate for the filter expression.
    /// </summary>
    /// <remarks>
    /// AOT-safe: this intentionally does <b>not</b> use <c>Expression.Lambda(...).Compile()</c>,
    /// which emits dynamic IL at runtime and is unsupported under Native AOT / IL trimming.
    /// The previous expression-tree path only ever produced a single call to
    /// <see cref="FilterResult.Evaluate"/>, so we capture the evaluated <see cref="FilterResult"/>
    /// in a plain closure and invoke it directly — same behavior, zero dynamic codegen.
    /// Future pattern-specialization (level/category/property fast paths) should be expressed
    /// as hand-written delegates here, not via expression compilation.
    /// </remarks>
    /// <param name="expression">The filter expression to compile</param>
    /// <returns>Evaluation delegate</returns>
    private static Func<LogEntry, LogFilterContext, bool> CompileToDelegate(FilterExpression expression)
    {
        // Evaluate the expression once against a context to obtain the FilterResult,
        // then close over it. No expression trees, no runtime IL generation.
        var filterContext = new FilterExpressionContext();
        var filterResult = expression(filterContext);

        return filterResult.Evaluate;
    }

    /// <summary>
    /// Generates an int hash for caching compiled expressions.
    /// Returns the raw HashCode int — no string allocation on the hot path.
    /// </summary>
    /// <param name="expression">The expression to hash</param>
    /// <returns>Hash code for caching</returns>
    private static int GetExpressionHash(FilterExpression expression)
    {
        var method = expression.Method;
        var target = expression.Target;

        return HashCode.Combine(
            method.DeclaringType?.FullName ?? "",
            method.Name,
            method.ToString(),
            target?.GetHashCode() ?? 0
        );
    }

    /// <summary>
    /// Clears the compilation cache.
    /// </summary>
    public static void ClearCache()
    {
        CompilationCache.Clear();
    }

    /// <summary>
    /// Gets compilation statistics.
    /// </summary>
    public static (int CachedExpressions, int TotalSize) GetStatistics()
    {
        return (CompilationCache.Count, CompilationCache.Values.Sum(v => v.IsValid ? 1 : 0));
    }
}