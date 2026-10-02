namespace Pragmatic.Persistence.Lifecycle;

/// <summary>
///     One entity property that carries a generated value, in a shape the unit of work can apply
///     without knowing the entity type.
/// </summary>
/// <remarks>
///     <para>
///         Produced by the create mutation's invoker, a generated value would be missing from an
///         entity created any other way — an action writing through a repository, an import, a
///         handler — which would reach the database with the column empty and nothing saying so. The
///         binding exists so the value is filled where every write converges instead.
///     </para>
///     <para>
///         Applied by <c>EfCoreUnitOfWork.SaveChangesAsync</c>, before it hands over to EF, and
///         deliberately not by a <c>SaveChanges</c> interceptor. A sequence-backed format has to ask
///         the database for the next value, and by the time an interceptor runs the context's
///         connection is the save's: doing it there hangs the process. An interceptor can
///         read and write tracked state; it cannot talk to the database.
///     </para>
///     <para>
///         Registered per property rather than per entity: an entity may carry more than one
///         generated value, and <c>IDefaultValueGenerator</c> is keyed by entity and value type,
///         which cannot tell two string properties apart.
///     </para>
/// </remarks>
public interface IGeneratedValueBinding
{
    /// <summary>The entity type this binding applies to, for diagnostics.</summary>
    Type EntityType { get; }

    /// <summary>The property this binding fills, for diagnostics.</summary>
    string PropertyName { get; }

    /// <summary>
    ///     Fills the property when the entity is of the bound type and the value is still empty.
    /// </summary>
    /// <param name="entity">The entity about to be inserted.</param>
    /// <param name="context">The lifecycle context handed to the generator.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///     <c>true</c> when this binding wrote a value. <c>false</c> when the entity is of another
    ///     type, or the value was already set — a caller that supplied its own is never overwritten,
    ///     for the same reason an import keeps the <c>CreatedBy</c> it carries.
    /// </returns>
    Task<bool> TryFillAsync(object entity, LifecycleContext context, CancellationToken ct);
}
