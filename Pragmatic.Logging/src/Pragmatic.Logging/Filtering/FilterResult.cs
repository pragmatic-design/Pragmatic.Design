using System.Runtime.CompilerServices;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Filtering;

/// <summary>
/// Represents the result of a filter expression evaluation with support for logical operators.
/// This struct enables natural expression syntax like: filter.A() &amp;&amp; filter.B() || !filter.C()
/// </summary>
public readonly struct FilterResult
{
    private readonly Func<LogEntry, LogFilterContext, bool>? _evaluator;

    /// <summary>
    /// Creates a filter result with the specified evaluation function.
    /// </summary>
    /// <param name="evaluator">Function that evaluates the filter condition</param>
    public FilterResult(Func<LogEntry, LogFilterContext, bool>? evaluator)
    {
        _evaluator = evaluator;
    }

    /// <summary>
    /// Creates a filter result with a constant boolean value.
    /// </summary>
    /// <param name="value">The constant boolean result</param>
    public FilterResult(bool value)
    {
        _evaluator = value ? AlwaysTrue : AlwaysFalse;
    }

    /// <summary>
    /// Evaluates this filter result against the specified log entry and context.
    /// </summary>
    /// <param name="logEntry">The log entry to evaluate</param>
    /// <param name="context">The filter context</param>
    /// <returns>True if the filter condition is met</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Evaluate(LogEntry logEntry, LogFilterContext context)
    {
        return _evaluator?.Invoke(logEntry, context) ?? false;
    }

    /// <summary>
    /// Logical AND operator - both conditions must be true.
    /// Implements short-circuit evaluation for performance.
    /// </summary>
    /// <param name="left">Left operand</param>
    /// <param name="right">Right operand</param>
    /// <returns>FilterResult representing left AND right</returns>
    public static FilterResult operator &(FilterResult left, FilterResult right)
    {
        // Optimization: if either operand is constant false, return false immediately
        if (left._evaluator == AlwaysFalse || right._evaluator == AlwaysFalse)
            return False;

        // Optimization: if left is always true, return right
        if (left._evaluator == AlwaysTrue)
            return right;

        // Optimization: if right is always true, return left  
        if (right._evaluator == AlwaysTrue)
            return left;

        // Create short-circuit AND evaluation
        return new FilterResult((entry, context) =>
            left.Evaluate(entry, context) && right.Evaluate(entry, context));
    }

    // Note: C# doesn't allow overloading && and || operators directly.
    // The & and | operators will be used, which provide the same functionality
    // with slightly different precedence rules.

    /// <summary>
    /// Logical OR operator - at least one condition must be true.
    /// Implements short-circuit evaluation for performance.
    /// </summary>
    /// <param name="left">Left operand</param>
    /// <param name="right">Right operand</param>
    /// <returns>FilterResult representing left OR right</returns>
    public static FilterResult operator |(FilterResult left, FilterResult right)
    {
        // Optimization: if either operand is constant true, return true immediately
        if (left._evaluator == AlwaysTrue || right._evaluator == AlwaysTrue)
            return True;

        // Optimization: if left is always false, return right
        if (left._evaluator == AlwaysFalse)
            return right;

        // Optimization: if right is always false, return left
        if (right._evaluator == AlwaysFalse)
            return left;

        // Create short-circuit OR evaluation
        return new FilterResult((entry, context) =>
            left.Evaluate(entry, context) || right.Evaluate(entry, context));
    }

    // Note: Use | operator for OR logic (|| is not overloadable in C#)

    /// <summary>
    /// Logical NOT operator - inverts the result.
    /// </summary>
    /// <param name="operand">The operand to negate</param>
    /// <returns>FilterResult representing NOT operand</returns>
    public static FilterResult operator !(FilterResult operand)
    {
        // Optimization: invert constant values immediately
        if (operand._evaluator == AlwaysTrue)
            return False;
        if (operand._evaluator == AlwaysFalse)
            return True;

        // Create negation evaluation
        return new FilterResult((entry, context) =>
            !operand.Evaluate(entry, context));
    }

    /// <summary>
    /// Implicit conversion from boolean to FilterResult.
    /// </summary>
    /// <param name="value">The boolean value</param>
    /// <returns>FilterResult with constant value</returns>
    public static implicit operator FilterResult(bool value) => new(value);

    /// <summary>
    /// Defines the true operator to enable short-circuit evaluation.
    /// </summary>
    /// <param name="operand">The operand to test</param>
    /// <returns>True if the operand would evaluate to true</returns>
    public static bool operator true(FilterResult operand)
    {
        return operand._evaluator == AlwaysTrue;
    }

    /// <summary>
    /// Defines the false operator to enable short-circuit evaluation.
    /// </summary>
    /// <param name="operand">The operand to test</param>
    /// <returns>True if the operand would evaluate to false</returns>
    public static bool operator false(FilterResult operand)
    {
        return operand._evaluator == AlwaysFalse;
    }

    /// <summary>
    /// Creates a FilterResult that always evaluates to true.
    /// </summary>
    public static FilterResult True => new(true);

    /// <summary>
    /// Creates a FilterResult that always evaluates to false.
    /// </summary>
    public static FilterResult False => new(false);

    /// <summary>
    /// Combines multiple FilterResults with AND logic.
    /// </summary>
    /// <param name="results">FilterResults to combine</param>
    /// <returns>FilterResult that is true only if all inputs are true</returns>
    public static FilterResult All(params FilterResult[] results)
    {
        if (results.Length == 0)
            return True;
        if (results.Length == 1)
            return results[0];

        var combined = results[0];
        for (int i = 1; i < results.Length; i++)
        {
            combined = combined & results[i];
        }
        return combined;
    }

    /// <summary>
    /// Combines multiple FilterResults with OR logic.
    /// </summary>
    /// <param name="results">FilterResults to combine</param>
    /// <returns>FilterResult that is true if any input is true</returns>
    public static FilterResult Any(params FilterResult[] results)
    {
        if (results.Length == 0)
            return False;
        if (results.Length == 1)
            return results[0];

        var combined = results[0];
        for (int i = 1; i < results.Length; i++)
        {
            combined = combined | results[i];
        }
        return combined;
    }

    // Cached constant evaluators for performance
    private static readonly Func<LogEntry, LogFilterContext, bool> AlwaysTrue = (_, _) => true;
    private static readonly Func<LogEntry, LogFilterContext, bool> AlwaysFalse = (_, _) => false;

    /// <summary>
    /// Returns a string representation of this FilterResult for debugging.
    /// </summary>
    /// <returns>String representation</returns>
    public override string ToString()
    {
        if (_evaluator == AlwaysTrue)
            return "FilterResult(True)";
        if (_evaluator == AlwaysFalse)
            return "FilterResult(False)";
        return "FilterResult(Expression)";
    }
}