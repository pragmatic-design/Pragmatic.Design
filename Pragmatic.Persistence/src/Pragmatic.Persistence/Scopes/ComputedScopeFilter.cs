using System.Linq.Expressions;
using Pragmatic.Authorization;
using Pragmatic.Identity;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Query.Filters;

namespace Pragmatic.Persistence.Scopes;

/// <summary>
///     Runtime query filter for entities with <see cref="DataScopeRule{T}"/> rules
///     using <see cref="ScopeStrategy.Computed"/> or <see cref="ScopeStrategy.Hybrid"/>.
///     OR-composes the expressions of all rules whose scopes the user has access to.
/// </summary>
/// <typeparam name="T">The entity type (must implement <see cref="IScopedEntity"/>).</typeparam>
public sealed class ComputedScopeFilter<T>(
    IEnumerable<DataScopeRule<T>> rules,
    IUserScopeResolver scopeResolver,
    ICurrentUser currentUser)
    : IQueryFilter<T>, Query.Filters.IScopeVisibilityFilter
    where T : class, IScopedEntity
{
    /// <summary>
    ///     Priority 260 — after ScopedDataFilter (250) to layer computed rules on top.
    /// </summary>
    public int Priority => 260;

    public Expression<Func<T, bool>> GetFilter()
    {
        // Resolve scopes synchronously via a dedicated sync-safe method to avoid deadlocks.
        // IUserScopeResolver implementations must expose ResolveAccessScopes or support
        // synchronous resolution. Fall back to empty set when only async is available.
        var userScopes = scopeResolver.ResolveAccessScopes(currentUser);

        // Collect rules where: strategy is Computed or Hybrid AND the user has the scope
        var applicableRules = rules
            .Where(r => r.Strategy is ScopeStrategy.Computed or ScopeStrategy.Hybrid)
            .Where(r => userScopes.Contains($"scope:{r.ScopeName}"))
            .ToList();

        if (applicableRules.Count == 0)
        {
            // No computed rules match → pass through (don't filter)
            return _ => true;
        }

        // OR-compose: entity matches if ANY applicable rule matches.
        // Inline expression body (replacing the rule's parameter with ours) instead of
        // Expression.Invoke so EF Core can translate the predicate to SQL.
        var parameter = Expression.Parameter(typeof(T), "entity");
        Expression? combined = null;

        foreach (var rule in applicableRules)
        {
            var ruleExpr = rule.ToExpression();
            // Replace rule's own parameter with our shared parameter — avoids Expression.Invoke
            var body = new ParameterReplacer(ruleExpr.Parameters[0], parameter).Visit(ruleExpr.Body);

            combined = combined is null
                ? body
                : Expression.OrElse(combined, body);
        }

        return Expression.Lambda<Func<T, bool>>(combined!, parameter);
    }

    /// <summary>Replaces one ParameterExpression with another throughout an expression tree.</summary>
    private sealed class ParameterReplacer(ParameterExpression source, ParameterExpression target)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == source ? target : base.VisitParameter(node);
    }
}
