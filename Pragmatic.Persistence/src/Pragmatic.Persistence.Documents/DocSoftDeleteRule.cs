using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.Documents;

/// <summary>
///     What a soft delete of a <see cref="PrincipalType" /> does to the <see cref="DependentType" /> documents that point at it, as a relation declared
///     it with <c>OnSoftDelete</c>: the documents engine's form of EF Core's <c>SoftDeleteReferrerRule</c>, as <c>SqlSoftDeleteRule</c> is the Sql
///     engine's. Registered by the generated module start (<see cref="DocSoftDeleteRules" />).
/// </summary>
/// <param name="PrincipalType">The entity whose soft delete the rule answers.</param>
/// <param name="DependentType">The entity that points at it, as a refusal names it.</param>
/// <param name="Behavior">What the soft delete does to the referrers.</param>
/// <param name="Referrers">
///     The live documents pointing at a principal's key, read through the dependent's repository on the unit of work (the caller's filters applied,
///     the soft-deleted documents left out) and tracked by it.
/// </param>
/// <param name="ClearKey">Empties a referrer's foreign key, for <see cref="DeleteBehavior.SetNull" />; null for the other behaviors.</param>
public sealed record DocSoftDeleteRule(
    Type PrincipalType,
    Type DependentType,
    DeleteBehavior Behavior,
    Func<DocUnitOfWork, Guid, CancellationToken, Task<List<object>>> Referrers,
    Action<object>? ClearKey);
