using System.Collections;
using System.Globalization;
using System.Text;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.Pipes;

namespace Pragmatic.Documents.Templating.Expressions;

/// <summary>
/// Evaluates a <see cref="TemplateExpression"/> AST against a <see cref="TemplateDataContext"/>.
/// </summary>
public sealed class ExpressionEvaluator(PipeRegistry? pipes = null)
{
    private readonly PipeRegistry _pipes = pipes ?? PipeRegistry.Default;

    // Bounds aggregate enumeration so a lazy/infinite IEnumerable source cannot spin forever.
    private const int MaxAggregateItems = 1_000_000;

    private static InvalidOperationException AggregateTooLarge(string path) => new(
        $"Aggregate over '{path}' exceeded the maximum of {MaxAggregateItems} items. " +
        "Materialise or page the collection before aggregating.");

    /// <summary>Evaluate an expression and return the raw value.</summary>
    public async ValueTask<object?> EvaluateAsync(
        TemplateExpression expression,
        TemplateDataContext context,
        CancellationToken ct = default)
    {
        return expression switch
        {
            LiteralExpression lit => lit.Value,
            PropertyAccessExpression prop => await context.ResolveAsync(prop.Path, ct),
            PipeExpression pipe => EvaluatePipe(pipe, await EvaluateAsync(pipe.Input, context, ct), context),
            BinaryExpression bin => await EvaluateBinaryAsync(bin, context, ct),
            TernaryExpression tern => await EvaluateTernaryAsync(tern, context, ct),
            NullCoalescingExpression nc => await EvaluateNullCoalesceAsync(nc, context, ct),
            AggregateExpression agg => await EvaluateAggregateAsync(agg, context, ct),
            InterpolatedStringExpression interp => await EvaluateInterpolatedAsync(interp, context, ct),
            TranslateExpression tr => await EvaluateTranslateAsync(tr, context, ct),
            _ => throw new InvalidOperationException($"Unknown expression type: {expression.GetType().Name}")
        };
    }

    /// <summary>Evaluate to string (for text content).</summary>
    public async ValueTask<string> EvaluateToStringAsync(
        TemplateExpression expression,
        TemplateDataContext context,
        CancellationToken ct = default)
    {
        var result = await EvaluateAsync(expression, context, ct);
        return result?.ToString() ?? "";
    }

    /// <summary>Evaluate to bool (for $if directives).</summary>
    public async ValueTask<bool> EvaluateToBoolAsync(
        TemplateExpression expression,
        TemplateDataContext context,
        CancellationToken ct = default)
    {
        var result = await EvaluateAsync(expression, context, ct);
        return IsTruthy(result);
    }

    private object? EvaluatePipe(PipeExpression pipe, object? input, TemplateDataContext context)
        => _pipes.Execute(pipe.PipeName, input, pipe.Args, context.Culture);

    private async ValueTask<object?> EvaluateBinaryAsync(BinaryExpression bin, TemplateDataContext ctx, CancellationToken ct)
    {
        var left = await EvaluateAsync(bin.Left, ctx, ct);

        // Short-circuit &&/|| : only evaluate the right operand when the left does not
        // already determine the result. This preserves C# semantics and avoids leaking
        // side effects/errors from the right side.
        switch (bin.Operator)
        {
            case BinaryOp.And:
                return IsTruthy(left) && IsTruthy(await EvaluateAsync(bin.Right, ctx, ct));
            case BinaryOp.Or:
                return IsTruthy(left) || IsTruthy(await EvaluateAsync(bin.Right, ctx, ct));
        }

        var right = await EvaluateAsync(bin.Right, ctx, ct);

        return bin.Operator switch
        {
            BinaryOp.Add => Add(left, right),
            BinaryOp.Sub => Arithmetic(left, right, (a, b) => a - b),
            BinaryOp.Mul => Arithmetic(left, right, (a, b) => a * b),
            BinaryOp.Div => Arithmetic(left, right, (a, b) => b != 0 ? a / b : 0),
            BinaryOp.Mod => Arithmetic(left, right, (a, b) => b != 0 ? a % b : 0),
            BinaryOp.Gt => Compare(left, right) > 0,
            BinaryOp.Lt => Compare(left, right) < 0,
            BinaryOp.Gte => Compare(left, right) >= 0,
            BinaryOp.Lte => Compare(left, right) <= 0,
            BinaryOp.Eq => Equals(left, right),
            BinaryOp.Neq => !Equals(left, right),
            _ => throw new InvalidOperationException($"Unknown operator: {bin.Operator}")
        };
    }

    private async ValueTask<object?> EvaluateTernaryAsync(TernaryExpression tern, TemplateDataContext ctx, CancellationToken ct)
    {
        var condition = await EvaluateToBoolAsync(tern.Condition, ctx, ct);
        return condition
            ? await EvaluateAsync(tern.TrueValue, ctx, ct)
            : await EvaluateAsync(tern.FalseValue, ctx, ct);
    }

    private async ValueTask<object?> EvaluateNullCoalesceAsync(NullCoalescingExpression nc, TemplateDataContext ctx, CancellationToken ct)
    {
        var left = await EvaluateAsync(nc.Left, ctx, ct);
        if (left is not null && !(left is string s && string.IsNullOrEmpty(s)))
            return left;
        return await EvaluateAsync(nc.Right, ctx, ct);
    }

    private async ValueTask<object?> EvaluateAggregateAsync(AggregateExpression agg, TemplateDataContext ctx, CancellationToken ct)
    {
        var collection = await ctx.ResolveCollectionAsync(agg.CollectionPath, ct);
        if (collection is null) return agg.Function == AggregateFunction.Count ? 0 : null;

        if (agg.Function == AggregateFunction.Count)
        {
            // Avoid materializing: use ICollection.Count when available, else enumerate once.
            if (collection is ICollection sized) return sized.Count;
            var n = 0;
            foreach (var _ in collection)
            {
                if (++n > MaxAggregateItems) throw AggregateTooLarge(agg.CollectionPath);
            }
            return n;
        }

        if (agg.PropertyName is null)
            return null;

        // Single pass: navigate property, coerce to double, fold into the aggregate accumulator.
        var count = 0;
        var seen = 0;
        double sum = 0, min = double.MaxValue, max = double.MinValue;
        foreach (var item in collection)
        {
            if (++seen > MaxAggregateItems) throw AggregateTooLarge(agg.CollectionPath);
            if (item is null) continue;
            var d = ToDouble(NavigateProperty(item, agg.PropertyName));
            if (d is null) continue;

            var v = d.Value;
            count++;
            sum += v;
            if (v < min) min = v;
            if (v > max) max = v;
        }

        if (count == 0) return null;

        return agg.Function switch
        {
            AggregateFunction.Sum => sum,
            AggregateFunction.Avg => sum / count,
            AggregateFunction.Min => min,
            AggregateFunction.Max => max,
            _ => null
        };
    }

    private async ValueTask<string> EvaluateInterpolatedAsync(InterpolatedStringExpression interp, TemplateDataContext ctx, CancellationToken ct)
    {
        var sb = new StringBuilder();
        foreach (var segment in interp.Segments)
        {
            switch (segment)
            {
                case TextSegment text:
                    sb.Append(text.Text);
                    break;
                case ExpressionSegment expr:
                    var value = await EvaluateAsync(expr.Expression, ctx, ct);
                    sb.Append(value?.ToString() ?? "");
                    break;
            }
        }
        return sb.ToString();
    }

    private async ValueTask<object?> EvaluateTranslateAsync(TranslateExpression tr, TemplateDataContext ctx, CancellationToken ct)
    {
        // Resolve parameters first
        Dictionary<string, object?>? resolvedParams = null;
        if (tr.Params is not null)
        {
            resolvedParams = [];
            foreach (var (name, expr) in tr.Params)
                resolvedParams[name] = await EvaluateAsync(expr, ctx, ct);
        }

        // Use ITranslationResolver if registered
        if (ctx.TranslationResolver is not null)
            return ctx.TranslationResolver.Resolve(tr.Key, resolvedParams, ctx.Culture);

        // Fallback: bracketed key for debugging
        if (resolvedParams is not null)
            return $"[{tr.Key}({string.Join(", ", resolvedParams.Select(kv => $"{kv.Key}={kv.Value}"))})]";

        return $"[{tr.Key}]";
    }

    // --- Helpers ---

    private static object? Add(object? left, object? right)
    {
        // String concatenation if either side is string
        if (left is string || right is string)
            return $"{left}{right}";
        return Arithmetic(left, right, (a, b) => a + b);
    }

    private static object? Arithmetic(object? left, object? right, Func<double, double, double> op)
    {
        var l = ToDouble(left);
        var r = ToDouble(right);
        if (l is null || r is null) return null;
        return op(l.Value, r.Value);
    }

    private static int Compare(object? left, object? right)
    {
        if (left is null && right is null) return 0;
        if (left is null) return -1;
        if (right is null) return 1;

        var l = ToDouble(left);
        var r = ToDouble(right);
        if (l is not null && r is not null) return l.Value.CompareTo(r.Value);

        return string.Compare(left.ToString(), right.ToString(), StringComparison.Ordinal);
    }

    private static new bool Equals(object? left, object? right)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;

        var l = ToDouble(left);
        var r = ToDouble(right);
        if (l is not null && r is not null) return Math.Abs(l.Value - r.Value) < 0.0001;

        return string.Equals(left.ToString(), right.ToString(), StringComparison.Ordinal);
    }

    // Every numeric zero and every empty collection is false, not only int, long and double: amounts are
    // decimal, and otherwise if="balance" would show a zero balance, and if="lines" an empty table's heading.
    private static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        int i => i != 0,
        long l => l != 0,
        double d => d != 0,
        decimal m => m != 0,
        float f => f != 0,
        short s => s != 0,
        byte b => b != 0,
        uint u => u != 0,
        ulong u => u != 0,
        string s => s.Length > 0,
        ICollection c => c.Count > 0,
        IEnumerable e => HasAny(e),
        _ => true
    };

    // One step, never the whole sequence: a lazy source may be long or endless.
    private static bool HasAny(IEnumerable sequence)
    {
        var enumerator = sequence.GetEnumerator();
        try
        {
            return enumerator.MoveNext();
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
    }

    private static double? ToDouble(object? value) => value switch
    {
        null => null,
        int i => i,
        long l => l,
        double d => d,
        decimal m => (double)m,
        float f => f,
        string s when double.TryParse(s, CultureInfo.InvariantCulture, out var d) => d,
        _ => null
    };

    private static object? NavigateProperty(object target, string propertyName)
    {
        // Dictionary access is direct and zero-reflection.
        if (target is IDictionary<string, object?> dict)
            return dict.TryGetValue(propertyName, out var v) ? v : null;

        // Non-dictionary types: no reflection fallback (zero-reflection rule).
        // Register aggregate sources as Dictionary<string,object?> or use a typed IPropertyAccessor
        // via TemplateDataContext.WithAccessor() to support property navigation.
        return null;
    }
}
