namespace Pragmatic.Actions.Mutation;

/// <summary>
///     Marks a class as a mutation — an orchestrated operation that modifies an entity
///     through the MutationInvoker pipeline.
/// </summary>
/// <remarks>
///     <para>
///         The source generator:
///         <list type="bullet">
///             <item>Generates a <c>MutationInvoker</c> with the full pipeline (validate → load/create → apply → entity validate → persist → events)</item>
///             <item>
///                 Generates auto-mapping (<c>ApplyToEntity</c>) whenever properties match, and the
///                 invoker calls it <b>before</b> <c>ApplyAsync</c> — overriding <c>ApplyAsync</c> does
///                 not replace it, so an override does not need to repeat every <c>SetX</c> by hand.
///             </item>
///             <item>Registers the invoker in DI</item>
///         </list>
///     </para>
///     <para>
///         <b>Mode detection</b>: If <see cref="Mode" /> is not set, the mode is inferred from
///         the class name: <c>Create{Entity}</c> → Create, <c>Update{Entity}</c> → Update,
///         <c>Delete{Entity}</c> → Delete.
///         If none matches, a diagnostic error is emitted.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [Mutation]  // Mode=Create inferred from "Create" prefix
/// [Validate]
/// public partial class CreateReservation : Mutation&lt;Reservation&gt;
/// {
///     [Required] public Guid GuestId { get; init; }
///     [FutureDate] public DateOnly CheckIn { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MutationAttribute : Attribute
{
    /// <summary>
    ///     Gets or sets the operation mode. If not specified, inferred from the class name
    ///     (<c>Create*</c> → Create, <c>Update*</c> → Update, <c>Delete*</c> → Delete).
    /// </summary>
    public MutationMode Mode { get; set; }

    /// <summary>
    ///     Gets or sets what the mutation returns after successful execution: the entity (the default),
    ///     its key, or its <c>[LogicKey]</c>. It decides both what the boundary member returns and
    ///     what the endpoint answers with.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Left unset, the endpoint does <b>not</b> answer with the entity: a create of an
    ///     entity answers its id (<c>{"id": …}</c>, 201), anything else answers nothing (204). The entity is
    ///     the persistence shape, loaded only as far as the mutation writes. The boundary member still
    ///     returns the entity. Set <c>ReturnType = MutationReturnType.Entity</c> explicitly, or declare
    ///     <c>[ReturnsDto&lt;T&gt;]</c>, to put a body on the wire.
    /// </remarks>
    public MutationReturnType ReturnType { get; set; } = MutationReturnType.Entity;

    /// <summary>
    ///     Gets or sets whether a Delete mutation performs a soft delete
    ///     (marks entity as deleted) instead of a hard delete (removes entity).
    ///     Only applicable when <see cref="Mode" /> is <see cref="MutationMode.Delete" />.
    /// </summary>
    public bool SoftDelete { get; set; }

    /// <summary>
    ///     Whether the operation is kept off the boundary's <b>public</b> interface. Leave it unset and
    ///     the presence of an <c>[Endpoint]</c> decides.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Three states, because there are three intents and only one of them is common:
    ///     </para>
    ///     <list type="table">
    ///         <item>
    ///             <term>unset</term>
    ///             <description>
    ///                 Inferred. With an <c>[Endpoint]</c> the operation is surface, so it is public;
    ///                 without one it is a step or a helper, so it stays on the <c>internal</c>
    ///                 interface. Not writing anything gives the answer that is right almost always.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <term><c>false</c></term>
    ///             <description>
    ///                 Public anyway. This is how an operation with no HTTP surface is offered to other
    ///                 modules — <c>IngestTextAction</c> is the case: never routed, called across a
    ///                 boundary every day.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <term><c>true</c></term>
    ///             <description>Internal anyway, endpoint or not.</description>
    ///         </item>
    ///     </list>
    ///     <para>
    ///         ⚠️ Unset is not <c>false</c> on purpose: with <c>false</c> as the default an operation
    ///         nobody meant to offer would sit on the interface other modules consume until someone
    ///         remembered a flag they did not know existed. Leaking by forgetting is the wrong way round.
    ///     </para>
    /// </remarks>
    /// <remarks>
    ///     Declared <c>bool</c> rather than <c>bool?</c> — a nullable is not a valid attribute argument
    ///     (CS0655). The three states are still there: the generator reads the attribute's named
    ///     arguments, so "written as false" and "not written" are different things to it, whatever the
    ///     language default is.
    /// </remarks>
    public bool Internal { get; set; }
}
