using System.Collections.Concurrent;
using Pragmatic.Persistence.Scopes;

namespace Pragmatic.Persistence.Sql.Pragmatic;

/// <summary>
///     The data scope rules (<see cref="DataScopeRule{T}" />) an engine's generator compiled: registered at module start by the assembly that
///     declares each rule. A read reads a Computed or Hybrid rule in its statement; a save matches a Materialized or Hybrid rule against the
///     row (<see cref="Matches" />). A rule registered in DI that no generator compiled is refused, never left out.
/// </summary>
public static class CompiledScopeRules
{
    private static readonly ConcurrentDictionary<Type, Func<DataScopeRule, Func<object, bool>>> Compilers = new();
    private static readonly ConcurrentDictionary<Type, Func<object, bool>> Matchers = new();
    private static readonly ConcurrentDictionary<Type, bool> Readable = new();

    /// <summary>
    ///     Called by the generated registration: the rule type, how its predicate becomes a delegate over the row (for the saves), and whether the
    ///     generator translated it into the reads' statements (<paramref name="readable" />: a Computed or Hybrid rule must be).
    /// </summary>
    public static void Register(Type rule, Func<DataScopeRule, Func<object, bool>> compile, bool readable)
    {
        Compilers.TryAdd(rule, compile);
        Readable.TryAdd(rule, readable);
    }

    /// <summary>True when the reads' statements carry the rule (its predicate translated by the generator).</summary>
    public static bool IsCompiled(Type rule) => Readable.TryGetValue(rule, out var readable) && readable;

    /// <summary>
    ///     True when <paramref name="entity" /> belongs to the rule's scope. The predicate is the rule's own tree, interpreted (no code emitted:
    ///     Native AOT), built once per rule type as Pragmatic's <c>ScopeMaterializer</c> compiles it once.
    /// </summary>
    public static bool Matches(DataScopeRule rule, object entity)
    {
        var type = rule.GetType();
        var matcher = Matchers.GetOrAdd(type, _ => Compilers.TryGetValue(type, out var compile)
            ? compile(rule)
            : throw NotCompiled(rule));
        return matcher(entity);
    }

    /// <summary>The refusal of a registered rule no generator compiled.</summary>
    public static NotSupportedException NotCompiled(DataScopeRule rule)
        => new($"The data scope rule {rule.GetType().Name} of {rule.EntityType.Name} is registered but was not compiled by this engine's generator: declare it in an assembly the generator runs on (its ToExpression an expression-bodied lambda).");
}
