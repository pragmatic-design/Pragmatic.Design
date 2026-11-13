using Microsoft.EntityFrameworkCore;
using DeleteBehavior = Pragmatic.Persistence.Entity.DeleteBehavior;

namespace Pragmatic.Persistence.EFCore.SoftDelete;

/// <summary>
///     What a soft delete of a principal does to the rows that point at it, as a relation declared it with
///     <c>OnSoftDelete</c>.
/// </summary>
/// <remarks>
///     <para>
///         The generated entity configuration puts one on the relation's foreign key, under
///         <see cref="Annotation" />, and <c>SoftDeleteInterceptor</c> reads it from the model when a principal
///         is soft-deleted. A relation that declares no <c>OnSoftDelete</c> carries none, and its referrers are
///         left as they are: <c>OnDelete</c> governs a physical delete only.
///     </para>
///     <para>
///         The query is typed and written by the generator, so finding the referrers needs no reflection: the
///         base class exists only so the interceptor can hold rules of every pair of types.
///     </para>
/// </remarks>
public abstract class SoftDeleteReferrerRule
{
    /// <summary>The model annotation, on the foreign key, that carries the rule.</summary>
    public const string Annotation = "Pragmatic:OnSoftDelete";

    private protected SoftDeleteReferrerRule(DeleteBehavior behavior) => Behavior = behavior;

    /// <summary>What the soft delete does to the referrers.</summary>
    public DeleteBehavior Behavior { get; }

    /// <summary>The entity that points at the principal, as the refusal names it.</summary>
    public abstract Type DependentType { get; }

    /// <summary>The rows pointing at <paramref name="principal" /> that the context's filters let it see.</summary>
    internal abstract List<object> Referrers(DbContext db, object principal);

    /// <inheritdoc cref="Referrers" />
    internal abstract Task<List<object>> ReferrersAsync(DbContext db, object principal, CancellationToken ct);
}
