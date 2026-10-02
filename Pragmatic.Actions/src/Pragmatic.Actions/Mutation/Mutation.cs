using Pragmatic.Result;

namespace Pragmatic.Actions.Mutation;

/// <summary>
///     Base class for mutations — orchestrated operations that modify a single entity
///     through the MutationInvoker pipeline.
/// </summary>
/// <typeparam name="TEntity">The entity type this mutation targets.</typeparam>
/// <remarks>
///     <para>
///         The MutationInvoker pipeline:
///         <list type="number">
///             <item>Input validation (Validate/IAsyncValidator on this mutation)</item>
///             <item>Load existing entity (Update) or create new (Create)</item>
///             <item>Call <see cref="ApplyAsync" /> (manual) or auto-generated <see cref="ApplyToEntity" /> (auto-map)</item>
///             <item>Entity validation (change-tracking-aware, on the entity)</item>
///             <item>Persist (Add if new, SaveChanges)</item>
///             <item>Dispatch domain events</item>
///         </list>
///     </para>
///     <para>
///         <b>Auto-mapping</b>: the source generator emits <see cref="ApplyToEntity" />, mapping matching
///         properties via <c>entity.SetXxx(this.Xxx)</c>. It is emitted whether or not you override
///         <see cref="ApplyAsync" />, and the invoker calls it first.
///     </para>
///     <para>
///         <b>Manual override</b>: Override <see cref="ApplyAsync" /> for custom logic (state transitions,
///         complex business rules). Auto-mapped properties are applied BEFORE your override runs,
///         so you only need to handle the custom logic — no need to repeat SetX() calls for mapped properties.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // Auto-mapped (generator generates ApplyToEntity)
/// [Mutation]
/// public partial class CreateReservation : Mutation&lt;Reservation&gt;
/// {
///     public Guid GuestId { get; init; }
///     public DateOnly CheckIn { get; init; }
/// }
///
/// // Manual (developer overrides ApplyAsync)
/// [Mutation(Mode = MutationMode.Update)]
/// public partial class CheckInGuest : Mutation&lt;Reservation, ConflictError&gt;
/// {
///     public required Guid Id { get; init; }
///     public override Task&lt;Result&lt;Reservation, IError&gt;&gt; ApplyAsync(
///         Reservation entity, CancellationToken ct)
///     {
///         return Task.FromResult(entity.TransitionTo(ReservationStatus.CheckedIn)
///             .Map(_ =&gt; entity));
///     }
/// }
/// </code>
/// </example>
public abstract class Mutation<TEntity>
    where TEntity : class
{
    /// <summary>
    ///     Applies auto-generated property mappings to the entity via SetX() calls.
    ///     The source generator overrides this with concrete property mappings.
    ///     Called by the MutationInvoker BEFORE <see cref="ApplyAsync" />, ensuring
    ///     auto-mapping always runs even when the dev overrides ApplyAsync.
    /// </summary>
    /// <param name="entity">The entity to apply property values to.</param>
    public virtual void ApplyToEntity(TEntity entity)
    {
    }

    /// <summary>
    ///     Applies custom mutation logic to the entity. Override this for state transitions,
    ///     complex business rules, or child entity creation.
    ///     Auto-mapped properties are ALREADY applied before this method is called —
    ///     no need to call base.ApplyAsync() for property mapping.
    /// </summary>
    /// <param name="entity">The entity with auto-mapped properties already set.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The modified entity wrapped in a Result, or an error.</returns>
    public virtual Task<Result<TEntity, IError>> ApplyAsync(TEntity entity, CancellationToken ct = default)
    {
        return Task.FromResult<Result<TEntity, IError>>(entity);
    }
}
