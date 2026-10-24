using Pragmatic.Persistence.Engines;
using Pragmatic.Persistence.Scopes;

namespace Pragmatic.Persistence.Sql.Pragmatic;

/// <summary>
///     The data scope rules the Sql engine's generator compiled (<see cref="CompiledScopeRuleRegistry" />): registered at module start by the
///     assembly that declares each rule, read by this engine's filters and saves only.
/// </summary>
public static class CompiledScopeRules
{
    /// <summary>The Sql engine's registry.</summary>
    internal static CompiledScopeRuleRegistry Registry { get; } = new();

    /// <inheritdoc cref="CompiledScopeRuleRegistry.Register" />
    public static void Register(Type rule, Func<DataScopeRule, Func<object, bool>> compile, bool readable) => Registry.Register(rule, compile, readable);

    /// <inheritdoc cref="CompiledScopeRuleRegistry.IsCompiled" />
    public static bool IsCompiled(Type rule) => Registry.IsCompiled(rule);

    /// <inheritdoc cref="CompiledScopeRuleRegistry.Matches" />
    public static bool Matches(DataScopeRule rule, object entity) => Registry.Matches(rule, entity);
}
