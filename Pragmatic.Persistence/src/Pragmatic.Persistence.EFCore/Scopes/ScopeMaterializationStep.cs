using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Scopes;

namespace Pragmatic.Persistence.EFCore.Scopes;

/// <summary>
///     The <see cref="IScopeMaterializationStep" /> for one scoped entity type.
/// </summary>
/// <typeparam name="TEntity">An entity carrying <c>[HasAccessScopes]</c>.</typeparam>
/// <param name="materializer">Evaluates the registered <c>DataScopeRule&lt;TEntity&gt;</c> against a row.</param>
/// <remarks>
///     <para>
///         <b>Added and Modified, and the second one is the point.</b> A materialized scope is a function
///         of the row's data, so an invoice whose currency changes from EUR to USD has to lose
///         <c>scope:billing-eu</c> and gain <c>scope:billing-usd</c> — which is why
///         <see cref="IScopeMaterializer" /> removes as well as adds. Leaving the old one behind keeps the
///         row visible to a department it left.
///     </para>
///     <para>
///         ⚠️ This is the opposite of the creator's stamp beside it, which is insert-only: re-stamping an
///         update would hand the row to the last person who touched it. The two run in one interceptor,
///         in a fixed order, for exactly that reason.
///     </para>
/// </remarks>
public sealed class ScopeMaterializationStep<TEntity>(IScopeMaterializer materializer)
    : IScopeMaterializationStep
    where TEntity : class, IScopedEntity
{
    /// <inheritdoc />
    public void Materialize(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries<TEntity>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            materializer.Materialize(entry.Entity);
        }
    }
}
