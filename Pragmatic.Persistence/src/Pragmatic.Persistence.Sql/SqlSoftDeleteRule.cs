using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     What a soft delete of a <see cref="PrincipalType" /> does to the <see cref="DependentType" /> rows that point at it, as a relation declared
///     it with <c>OnSoftDelete</c>: the Sql engine's form of EF Core's <c>SoftDeleteReferrerRule</c>. Registered by the generated module start
///     (<see cref="SqlSoftDeleteRules" />).
/// </summary>
/// <param name="PrincipalType">The entity whose soft delete the rule answers.</param>
/// <param name="DependentType">The entity that points at it, as a refusal names it.</param>
/// <param name="Behavior">What the soft delete does to the referrers.</param>
/// <param name="Referrers">
///     The live rows pointing at a principal's key, read through the dependent's repository on the unit of work (the caller's filters applied, the
///     soft-deleted rows left out) and tracked by it.
/// </param>
/// <param name="ClearKey">Empties a referrer's foreign key, for <see cref="DeleteBehavior.SetNull" />; null for the other behaviors.</param>
public sealed record SqlSoftDeleteRule(
    Type PrincipalType,
    Type DependentType,
    DeleteBehavior Behavior,
    Func<SqlUnitOfWork, Guid, CancellationToken, Task<List<object>>> Referrers,
    Action<object>? ClearKey);
