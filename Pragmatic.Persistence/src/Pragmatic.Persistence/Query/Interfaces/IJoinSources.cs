using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.Query.Interfaces;

/// <summary>
///     The sets a query's declared joins read.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="IQuery{TEntity}.Apply" /> receives the root entity's set and can reach anything a
///         navigation leads to. A <c>[Join&lt;T&gt;(ForeignKey = …)]</c> exists for the case where there
///         is no navigation to follow, so the target's set has to arrive from outside — this is that
///         channel, and the executor is what hands it over.
///     </para>
///     <para>
///         ⚠️ The type argument is written by the generator, which knows the target at compile time.
///         Nothing here resolves a set from a name or from a <see cref="System.Type" />.
///     </para>
/// </remarks>
public interface IJoinSources
{
    /// <summary>The queryable set of <typeparamref name="TEntity" />.</summary>
    /// <typeparam name="TEntity">
    ///     The joined entity type, named by the generated query. Annotated because the implementation
    ///     hands it to EF Core's <c>Set&lt;T&gt;</c>, which materialises rows: the requirement is
    ///     declared here so it reaches the caller instead of failing in a trimmed build.
    /// </typeparam>
    IQueryable<TEntity> Of<
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors
            | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors
            | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields
            | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicFields
            | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties
            | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties
            | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.Interfaces)]
        TEntity>() where TEntity : class, IEntity;
}
