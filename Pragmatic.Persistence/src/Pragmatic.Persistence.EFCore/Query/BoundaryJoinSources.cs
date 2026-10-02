using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Query.Interfaces;

namespace Pragmatic.Persistence.EFCore.Query;

/// <summary>
///     The sets a boundary's <c>DbContext</c> holds, for the joins a query declares.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>Same context, or no join.</b> EF Core composes a <c>Join</c> only between queryables
///         of one <c>DbContext</c> instance, and a Pragmatic host builds one per boundary. So the
///         target is read from the root boundary's own context — the keyed registration
///         <c>DomainActionInvoker</c> already uses — and never from a repository, which would resolve
///         the <i>owning</i> boundary's context and produce a join EF cannot translate.
///     </para>
///     <para>
///         The target therefore has to be in that boundary's model: its own entity, or another
///         boundary's brought in by <c>[ReadAccess&lt;T&gt;]</c>. When it is not, <c>Set&lt;T&gt;</c>
///         raises, and the message below says which declaration to add rather than leaving the
///         author with EF Core's.
///     </para>
/// </remarks>
/// <param name="services">The scope the query is being executed in.</param>
public sealed class BoundaryJoinSources(IServiceProvider services) : IJoinSourceProvider
{
    /// <inheritdoc />
    public IJoinSources ForBoundary<TBoundary>() where TBoundary : class
        => new ContextSets(
            services.GetRequiredKeyedService<DbContext>(typeof(TBoundary)),
            typeof(TBoundary).Name);

    private sealed class ContextSets(DbContext context, string boundaryName) : IJoinSources
    {
        public IQueryable<TEntity> Of<
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
                System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors
                | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors
                | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields
                | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicFields
                | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties
                | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties
                | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.Interfaces)]
            TEntity>() where TEntity : class, IEntity
        {
            if (context.Model.FindEntityType(typeof(TEntity)) is null)
            {
                throw new InvalidOperationException(
                    $"A declared [Join<{typeof(TEntity).Name}>] cannot read it: {typeof(TEntity).Name} is not in "
                    + $"{boundaryName}'s model. Add [ReadAccess<{typeof(TEntity).Name}>] to {boundaryName} — a join "
                    + "has to reach the target through the same DbContext the query's own source came from.");
            }

            return context.Set<TEntity>();
        }
    }
}
