namespace Pragmatic.Actions.Attributes;

/// <summary>
///     Declares, on a boundary, that <typeparamref name="TEntity" /> is one of its own.
/// </summary>
/// <typeparam name="TEntity">The entity this boundary writes and whose table lives in its context.</typeparam>
/// <remarks>
///     <para>
///         <b>The boundary claims the entity; the entity never names a boundary.</b> That direction is
///         what keeps a folder of entities portable: copy it into another project and it belongs to
///         whatever boundary is there, with nothing to edit. An attribute on the entity would name a
///         type of its old project and not compile.
///     </para>
///     <para>
///         <b>Only needed where there is a choice.</b> An assembly with one boundary — which is every
///         module until it says otherwise — needs none of these: that boundary owns every entity the
///         assembly declares. Write them when the assembly declares two or more, and then write them
///         for all of them: an entity no boundary claims is <c>PRAG0629</c>, and one that two claim is
///         <c>PRAG0630</c>.
///     </para>
///     <para>
///         The mirror of <see cref="ReadAccessAttribute{TEntity}" />, which names an entity belonging to
///         someone else. One says "mine", the other says "I only read yours".
///     </para>
///     <example>
///         <code>
/// [Boundary]
/// [Owns&lt;Invoice&gt;]
/// [Owns&lt;Payment&gt;]
/// [ReadAccess&lt;Reservation&gt;]
/// public partial class BillingBoundary;
/// </code>
///     </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class OwnsAttribute<TEntity> : Attribute
    where TEntity : class;
