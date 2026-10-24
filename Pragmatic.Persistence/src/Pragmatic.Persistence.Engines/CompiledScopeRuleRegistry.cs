using System.Collections.Concurrent;
using Pragmatic.Persistence.Scopes;

namespace Pragmatic.Persistence.Engines;

/// <summary>
///     The data scope rules (<see cref="DataScopeRule{T}" />) one engine's generator compiled: registered at module start by the assembly that
///     declares each rule. A read reads a Computed or Hybrid rule in its statement; a save matches a Materialized or Hybrid rule against the
///     row (<see cref="Matches" />). A rule registered in DI that no generator compiled is refused, never left out.
/// </summary>
/// <remarks>
///     One registry per engine, each bridge's own (the Sql engine's <c>CompiledScopeRules</c>): whether a rule is in the reads' statements
///     is a fact about one engine's generated code, and a rule the Sql generator compiled is not compiled for the documents engine.
/// </remarks>
public sealed class CompiledScopeRuleRegistry
{
    private readonly ConcurrentDictionary<Type, Func<DataScopeRule, Func<object, bool>>> _compilers = new();
    private readonly ConcurrentDictionary<Type, Func<object, bool>> _matchers = new();
    private readonly ConcurrentDictionary<Type, bool> _readable = new();

    /// <summary>
    ///     Called by the generated registration: the rule type, how its predicate becomes a delegate over the row (for the saves), and whether the
    ///     generator translated it into the reads' statements (<paramref name="readable" />: a Computed or Hybrid rule must be).
    /// </summary>
    public void Register(Type rule, Func<DataScopeRule, Func<object, bool>> compile, bool readable)
    {
        _compilers.TryAdd(rule, compile);
        _readable.TryAdd(rule, readable);
    }

    /// <summary>True when the reads' statements carry the rule (its predicate translated by the generator).</summary>
    public bool IsCompiled(Type rule) => _readable.TryGetValue(rule, out var readable) && readable;

    /// <summary>
    ///     True when <paramref name="entity" /> belongs to the rule's scope. The predicate is the rule's own tree, interpreted (no code emitted:
    ///     Native AOT), built once per rule type as Pragmatic's <c>ScopeMaterializer</c> compiles it once.
    /// </summary>
    public bool Matches(DataScopeRule rule, object entity)
    {
        var type = rule.GetType();
        var matcher = _matchers.GetOrAdd(type, _ => _compilers.TryGetValue(type, out var compile)
            ? compile(rule)
            : throw NotCompiled(rule));
        return matcher(entity);
    }

    /// <summary>The refusal of a registered rule no generator compiled.</summary>
    public static NotSupportedException NotCompiled(DataScopeRule rule)
        => new($"The data scope rule {rule.GetType().Name} of {rule.EntityType.Name} is registered but was not compiled by this engine's generator: declare it in an assembly the generator runs on (its ToExpression an expression-bodied lambda).");
}
