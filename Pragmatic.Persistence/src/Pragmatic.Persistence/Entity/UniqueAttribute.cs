namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Declares a unique index over one or more of the entity's properties.
/// </summary>
/// <remarks>
///     <para>
///         <b>Not the same thing as <see cref="LogicKeyAttribute" />.</b> A domain key is how the
///         entity is identified in the language of the domain: there is one, and the generator gives
///         it a unique index <em>plus</em> a <c>GetBy…Async</c> on the repository and a specification.
///         This declares a constraint and nothing else — as many as the entity needs, with no accessor
///         generated for any of them. Reach for <c>[LogicKey]</c> when the answer to "how do you look
///         this up" is these columns; reach for this when the answer is "you don't, they just cannot
///         repeat".
///     </para>
///     <para>
///         Before this existed an entity could declare exactly one unique index, because
///         <c>[LogicKey]</c> was the only way to declare any — so a second constraint had to be
///         written by hand in <c>OnModelCreating</c>, where the migrations do not see it, or left
///         unenforced.
///     </para>
///     <para>
///         The columns are named with <c>nameof</c> and the order is the order written: a composite
///         index can only be searched by its leading columns, so the first name is the one that
///         decides what the index is good for beyond the constraint itself.
///     </para>
///     <example>
///         <code>
/// [Entity]
/// [Unique(nameof(Email))]
/// [Unique(nameof(ExternalCode), Scope = UniquenessScope.Global)]
/// public partial class Member : IEntity, ITenantEntity
/// {
///     public string Email { get; private set; } = "";
///     public string ExternalCode { get; private set; } = "";
/// }
/// </code>
///     </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class UniqueAttribute : Attribute
{
    /// <summary>Declares the index over the named properties, in the order given.</summary>
    /// <param name="propertyNames">
    ///     The properties the index covers. Use <c>nameof</c>: a name that matches no property is
    ///     <c>PRAG0627</c> rather than an index that quietly never gets created.
    /// </param>
    public UniqueAttribute(params string[] propertyNames) => PropertyNames = propertyNames;

    /// <summary>The properties the index covers, in the order written.</summary>
    public string[] PropertyNames { get; }

    /// <summary>
    ///     How far the index has to be unique. Only meaningful on an entity that implements
    ///     <c>ITenantEntity</c>; everywhere else there is one scope and this changes nothing.
    /// </summary>
    /// <remarks>
    ///     Per-tenant by default, and for the same reason <see cref="LogicKeyAttribute.Scope" /> is:
    ///     uniqueness that reaches across tenants lets one of them take a value away from the others,
    ///     and tell them so by refusing it.
    /// </remarks>
    public UniquenessScope Scope { get; set; } = UniquenessScope.PerTenant;
}
