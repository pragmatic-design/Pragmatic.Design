using Microsoft.EntityFrameworkCore;
using DeleteBehavior = Pragmatic.Persistence.Entity.DeleteBehavior;

namespace Pragmatic.Persistence.EFCore.SoftDelete;

/// <summary>
///     The rule for one relation, typed by its two ends: the generator writes the query for the referrers.
/// </summary>
/// <remarks>
///     A <c>SetNull</c> empties the key through the change tracker, by the foreign key's own properties, so the
///     rule carries no assignment: a generated key has no public setter to call.
/// </remarks>
/// <typeparam name="TPrincipal">The entity being soft-deleted.</typeparam>
/// <typeparam name="TDependent">The entity whose key points at it.</typeparam>
/// <param name="behavior">What the soft delete does to the referrers.</param>
/// <param name="referrers">The referrers of a principal, as a query over the context.</param>
public sealed class SoftDeleteReferrerRule<TPrincipal, TDependent>(
    DeleteBehavior behavior,
    Func<DbContext, TPrincipal, IQueryable<TDependent>> referrers) : SoftDeleteReferrerRule(behavior)
    where TPrincipal : class
    where TDependent : class
{
    /// <inheritdoc />
    public override Type DependentType => typeof(TDependent);

    /// <inheritdoc />
    internal override List<object> Referrers(DbContext db, object principal)
        => [.. referrers(db, (TPrincipal)principal)];

    /// <inheritdoc />
    internal override async Task<List<object>> ReferrersAsync(DbContext db, object principal, CancellationToken ct)
        => [.. await referrers(db, (TPrincipal)principal).ToListAsync(ct).ConfigureAwait(false)];
}
