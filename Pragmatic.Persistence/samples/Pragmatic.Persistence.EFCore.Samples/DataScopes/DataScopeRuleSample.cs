using System.Linq.Expressions;
using Pragmatic.Persistence.Scopes;

namespace Pragmatic.Persistence.EFCore.Samples.DataScopes;

/// <summary>
///     Demonstrates L3 data ownership: <see cref="DataScopeRule{T}"/> with both
///     <see cref="ScopeStrategy.Materialized"/> and <see cref="ScopeStrategy.Computed"/> strategies.
///     <list type="bullet">
///         <item>Materialized — the rule's compiled delegate stamps a scope token into
///               <c>AccessScopes</c> (what <c>ScopeMaterializer</c> does on save).</item>
///         <item>Computed — applicable rules' expression bodies are OR-composed at query time into a
///               single EF-translatable predicate (what <c>ComputedScopeFilter&lt;T&gt;</c> does).</item>
///     </list>
/// </summary>
public static class DataScopeRuleSample
{
    public static void Run()
    {
        Console.WriteLine("═══ L3 Data Scopes (DataScopeRule<T>: Materialized + Computed) ═══");
        Console.WriteLine();

        var invoices = new List<ScopedInvoice>
        {
            new() { Number = "INV-1", Currency = "EUR", Amount = 250m },
            new() { Number = "INV-2", Currency = "EUR", Amount = 5000m },
            new() { Number = "INV-3", Currency = "USD", Amount = 120m },
            new() { Number = "INV-4", Currency = "USD", Amount = 8000m }
        };

        var eurRule = new EurInvoiceScopeRule();
        var highValueRule = new HighValueInvoiceScopeRule();

        // ── Materialized: stamp scope tokens onto each row (ScopeMaterializer semantics) ──
        var eurMatches = eurRule.ToExpression().Compile();
        foreach (var inv in invoices)
            if (eurMatches(inv))
                inv.AccessScopes.Add($"scope:{eurRule.ScopeName}");

        Console.WriteLine("  Materialized 'eur-invoices' onto AccessScopes:");
        foreach (var inv in invoices)
            Console.WriteLine($"    {inv.Number} ({inv.Currency,3} {inv.Amount,7:C}) → [{string.Join(", ", inv.AccessScopes)}]");
        Console.WriteLine();

        // ── Computed: a user holding scope:high-value sees rows matching the computed rule, ──
        //    OR-composed with any materialized scopes they already hold.
        string[] userScopes = ["scope:high-value"];
        var rules = new DataScopeRule<ScopedInvoice>[] { eurRule, highValueRule };

        var predicate = BuildComputedPredicate(rules, userScopes);
        var visible = invoices.AsQueryable().Where(predicate).ToList();

        Console.WriteLine("  Computed filter for user holding [scope:high-value]:");
        Console.WriteLine($"    visible: {string.Join(", ", visible.Select(i => i.Number))} (expects INV-2, INV-4 — Amount > 1000)");
        Console.WriteLine();
    }

    /// <summary>
    ///     Mirrors <c>ComputedScopeFilter&lt;T&gt;.GetFilter()</c>: collect Computed/Hybrid rules the
    ///     user has access to, then OR-compose their expression BODIES under one shared parameter
    ///     (no <c>Expression.Invoke</c>, so the result is EF-translatable).
    /// </summary>
    private static Expression<Func<ScopedInvoice, bool>> BuildComputedPredicate(
        IReadOnlyList<DataScopeRule<ScopedInvoice>> rules,
        IReadOnlyCollection<string> userScopes)
    {
        var applicable = rules
            .Where(r => r.Strategy is ScopeStrategy.Computed or ScopeStrategy.Hybrid)
            .Where(r => userScopes.Contains($"scope:{r.ScopeName}"))
            .ToList();

        if (applicable.Count == 0)
            return _ => false;

        var parameter = Expression.Parameter(typeof(ScopedInvoice), "invoice");
        Expression? combined = null;

        foreach (var rule in applicable)
        {
            var ruleExpr = rule.ToExpression();
            var body = new ParameterReplacer(ruleExpr.Parameters[0], parameter).Visit(ruleExpr.Body);
            combined = combined is null ? body : Expression.OrElse(combined, body!);
        }

        return Expression.Lambda<Func<ScopedInvoice, bool>>(combined!, parameter);
    }

    private sealed class ParameterReplacer(ParameterExpression source, ParameterExpression target)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == source ? target : base.VisitParameter(node);
    }
}
