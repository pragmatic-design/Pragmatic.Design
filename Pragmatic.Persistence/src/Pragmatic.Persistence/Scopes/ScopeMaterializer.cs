using System.Collections.Concurrent;
using System.Linq.Expressions;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.Scopes;

/// <summary>
///     Default implementation of <see cref="IScopeMaterializer"/>.
///     Evaluates all registered <see cref="DataScopeRule{T}"/> rules and updates
///     the entity's <c>AccessScopes</c> collection accordingly.
/// </summary>
public sealed class ScopeMaterializer(IServiceProvider serviceProvider) : IScopeMaterializer
{
    // Cache compiled delegates per rule type to avoid per-entity recompilation
    private static readonly ConcurrentDictionary<Type, Delegate> CompiledDelegateCache = new();

    /// <inheritdoc />
    public void Materialize<T>(T entity) where T : class, IScopedEntity
    {
        var rules = GetRules<T>();
        if (rules.Count == 0)
            return;

        foreach (var rule in rules)
        {
            if (rule.Strategy is ScopeStrategy.Computed)
                continue; // Computed rules are evaluated at query time, not materialized

            var scopeId = $"scope:{rule.ScopeName}";
            var compiled = (Func<T, bool>)CompiledDelegateCache.GetOrAdd(
                rule.GetType(),
                _ => rule.ToExpression().Compile());
            var matches = compiled(entity);

            if (matches)
            {
                if (!entity.AccessScopes.Contains(scopeId))
                    entity.AccessScopes.Add(scopeId);
            }
            else
            {
                entity.AccessScopes.Remove(scopeId);
            }
        }
    }

    private List<DataScopeRule<T>> GetRules<T>() where T : class
    {
        // Resolve all registered rules for entity type T
        var rules = serviceProvider.GetService(typeof(IEnumerable<DataScopeRule<T>>));
        return rules is IEnumerable<DataScopeRule<T>> enumerable
            ? enumerable.ToList()
            : [];
    }
}
