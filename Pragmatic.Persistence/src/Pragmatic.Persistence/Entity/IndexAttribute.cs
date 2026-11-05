namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Declares a non-unique index over one or more of the entity's properties.
/// </summary>
/// <remarks>
///     <para>
///         The non-unique counterpart of <see cref="UniqueAttribute" />, read in the same places: EF Core's configuration and generated
///         schema, the Sql engine's schema, the documents engine's indexes, and the metadata a host reads a referenced module's entities
///         from. An index written only in an EF Core <c>IEntityTypeConfiguration</c> is seen by no other engine, and for a package's
///         entities by nothing at all.
///     </para>
///     <para>
///         The columns are named with <c>nameof</c> and the order is the order written: a composite index can only be searched by its
///         leading columns. Nothing is added to them: no tenant column, no filter.
///     </para>
///     <example>
///         <code>
/// [Entity]
/// [Index(nameof(UserId), nameof(RoleName))]
/// public partial class RoleAssignment : IEntity
/// {
///     public string UserId { get; private set; } = "";
///     public string RoleName { get; private set; } = "";
/// }
/// </code>
///     </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class IndexAttribute : Attribute
{
    /// <summary>Declares the index over the named properties, in the order given.</summary>
    /// <param name="propertyNames">
    ///     The properties the index covers. Use <c>nameof</c>: a name that matches no property is <c>PRAG0627</c> rather than an index
    ///     that quietly never gets created.
    /// </param>
    public IndexAttribute(params string[] propertyNames) => PropertyNames = propertyNames;

    /// <summary>The properties the index covers, in the order written.</summary>
    public string[] PropertyNames { get; }
}
