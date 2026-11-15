using System.Collections.Concurrent;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     The <see cref="SqlSoftDeleteRule" />s the generated module starts register, by principal type: what <see cref="SqlUnitOfWork" />'s save
///     applies to the referrers of the rows it soft-deletes.
/// </summary>
/// <remarks>
///     Static, as the entity maps are: a rule is a fact about two entity types, the same for every boundary and every unit of work. Registering the
///     same pair again (a module loaded twice) replaces it.
/// </remarks>
public static class SqlSoftDeleteRules
{
    private static readonly ConcurrentDictionary<(Type Principal, Type Dependent), SqlSoftDeleteRule> s_rules = new();

    /// <summary>Registers a rule; called by the generated module start.</summary>
    public static void Register(SqlSoftDeleteRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        s_rules[(rule.PrincipalType, rule.DependentType)] = rule;
    }

    /// <summary>The rules on the relations into <paramref name="principal" /> or a type it derives from.</summary>
    public static IEnumerable<SqlSoftDeleteRule> Into(Type principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        foreach (var rule in s_rules.Values)
        {
            if (rule.PrincipalType.IsAssignableFrom(principal))
                yield return rule;
        }
    }
}
