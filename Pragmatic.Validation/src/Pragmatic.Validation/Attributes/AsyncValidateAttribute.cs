namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Binds an async validator to a property or entity for change-aware invocation.
/// </summary>
/// <remarks>
///     <para>
///         When applied to a <b>property</b>, the validator fires only when that property is modified.
///         When applied to a <b>class</b>, the validator fires on any entity modification.
///     </para>
///     <para>
///         This enables the <see cref="CompositeValidator{T}" /> to skip expensive async validators
///         (database calls, external services) when the triggering property hasn't changed.
///     </para>
///     <para>
///         In create mode (<c>modifiedProperties = null</c>), all bound validators are invoked
///         regardless of binding level.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // Property-level: fires only when Email changes
/// [AsyncValidate&lt;EmailUniquenessValidator&gt;]
/// public string Email { get; private set; }
///
/// // Entity-level: fires on any modification
/// [AsyncValidate&lt;ReservationAvailabilityValidator&gt;]
/// public partial class Reservation { ... }
/// </code>
/// </example>
/// <typeparam name="TValidator">
///     The async validator type. Must implement <see cref="IAsyncValidator{T}" />.
/// </typeparam>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = true)]
public sealed class AsyncValidateAttribute<TValidator> : Attribute
    where TValidator : class
{
}
