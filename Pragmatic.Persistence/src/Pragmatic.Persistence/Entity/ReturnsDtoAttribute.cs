namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Declares the shape an operation answers with, replacing the one <see cref="ResourceAttribute"/>
///     scaffolds.
/// </summary>
/// <remarks>
///     <para>
///         It goes on the operation, not on the entity. <c>[Resource]</c> says which operations exist
///         and on what route — the only thing that is a property of the resource rather than of its
///         single operations. Everything else is declared on the partial part of the generated type,
///         alongside <c>[RequirePermission]</c> and the rest:
///     </para>
///     <example>
///         <code>
/// [ReturnsDto&lt;GuestSummary&gt;]
/// [RequirePermission(BookingPermissions.Guest.Read)]
/// public partial class ResourceReadGuestQuery;
/// </code>
///     </example>
///     <para>
///         Attributes on a partial class combine across its parts, so those two lines are the same ones
///         you would write on a hand-written query. The day the operation stops being scaffolded and
///         becomes one you write yourself, they do not change.
///     </para>
///     <para>
///         <typeparamref name="TDto"/> must carry <c>[MapFrom&lt;TEntity&gt;]</c> and
///         <c>[GenerateProjection]</c> for the entity behind the resource. A read query projects in the
///         database, so a DTO with no projection returns nothing at all rather than failing — which is
///         why the generator refuses it (PRAG2608) instead of letting it ship.
///     </para>
/// </remarks>
/// <typeparam name="TDto">The type the operation answers with.</typeparam>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ReturnsDtoAttribute<TDto> : Attribute;
