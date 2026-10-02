using System.Collections.Concurrent;

namespace Pragmatic.Persistence.Scopes;

/// <summary>
///     Static registry for data scope rules. Supports both explicit registration
///     (via <see cref="DataScopeServiceExtensions.AddDataScopeRule{TRule,T}"/>)
///     and self-registration via static constructors in SG-generated types.
/// </summary>
/// <remarks>
///     This registry provides compile-time-safe discovery of scope rules.
///     Types that self-register via static constructor are discovered when the CLR
///     first touches the type — no assembly scanning needed.
/// </remarks>
public static class ScopeRegistry
{
    private static readonly ConcurrentDictionary<Type, List<Type>> RulesByEntityType = new();

    /// <summary>
    ///     Registers a scope rule type for a given entity type.
    ///     Safe to call multiple times — duplicates are ignored.
    /// </summary>
    /// <typeparam name="TEntity">The entity type the rule applies to.</typeparam>
    /// <param name="ruleType">The concrete <see cref="DataScopeRule{T}"/> type.</param>
    public static void Register<TEntity>(Type ruleType) where TEntity : class
    {
        var rules = RulesByEntityType.GetOrAdd(typeof(TEntity), _ => new List<Type>());
        lock (rules)
        {
            if (!rules.Contains(ruleType))
                rules.Add(ruleType);
        }
    }

    /// <summary>
    ///     Gets a snapshot of all registered scope rule types for a given entity type.
    ///     Returns a copy to avoid exposing the live mutable list to concurrent callers.
    /// </summary>
    public static IReadOnlyList<Type> GetRules<TEntity>() where TEntity : class
    {
        if (!RulesByEntityType.TryGetValue(typeof(TEntity), out var rules))
            return Array.Empty<Type>();
        lock (rules)
            return rules.ToArray();
    }

    /// <summary>
    ///     Gets a snapshot of all registered scope rule types for a given entity type.
    ///     Returns a copy to avoid exposing the live mutable list to concurrent callers.
    /// </summary>
    public static IReadOnlyList<Type> GetRules(Type entityType)
    {
        if (!RulesByEntityType.TryGetValue(entityType, out var rules))
            return Array.Empty<Type>();
        lock (rules)
            return rules.ToArray();
    }

    /// <summary>
    ///     Checks whether any scope rules are registered for the given entity type.
    /// </summary>
    public static bool HasRules<TEntity>() where TEntity : class
        => RulesByEntityType.ContainsKey(typeof(TEntity));

    /// <summary>
    ///     Clears all registrations. For testing only.
    /// </summary>
    internal static void Clear() => RulesByEntityType.Clear();
}
