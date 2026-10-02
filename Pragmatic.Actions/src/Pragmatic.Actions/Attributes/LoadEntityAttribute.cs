namespace Pragmatic.Actions.Attributes;

/// <summary>
///     Marks a DomainAction or a Mutation to auto-load an entity before execution.
///     The generator creates a field for the loaded entity and a <c>PrepareActionAsync</c> /
///     <c>PrepareMutationAsync</c> override on the invoker that loads it from the repository — after
///     authorization — and answers 404 when the key names nothing.
/// </summary>
/// <remarks>
///     <para>
///         On a mutation the entity is loaded through the same unit of work the mutation saves, so what
///         <c>ApplyAsync</c> changes on it is written with the mutation's own row.
///     </para>
///     <para>
///         The key property is of the entity's key type (<c>PRAG0411</c> otherwise). Declared nullable —
///         <c>Guid? ManagerId</c> — it is an optional load: the field is nullable, nothing is read when the
///         key is null, and a key that names no row is still a 404.
///     </para>
///     <para>
///         Instead of a key, a named rule: <c>[LoadEntity&lt;Employee&gt;(Specification =
///         nameof(EmployeeSpecifications.ActiveWithNumber))]</c> reads the first row the specification matches,
///         its parameters bound by name to the operation's properties; no row is a 404.
///     </para>
///     <para>
///         By the domain key instead of the id: <c>[LoadEntity&lt;Employee&gt;(nameof(EmployeeNumber), By =
///         nameof(Employee.EmployeeNumber))]</c> — see <see cref="By" />.
///     </para>
/// </remarks>
/// <typeparam name="TEntity">The entity type to load.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class LoadEntityAttribute<TEntity> : Attribute where TEntity : class
{
    /// <summary>
    ///     Creates a LoadEntity attribute that reads the row by <see cref="Specification" />.
    /// </summary>
    public LoadEntityAttribute()
    {
    }

    /// <summary>
    ///     Creates a new LoadEntity attribute.
    /// </summary>
    /// <param name="idPropertyName">
    ///     The name of the property on the action or mutation that holds the entity ID.
    /// </param>
    public LoadEntityAttribute(string idPropertyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(idPropertyName);
        IdPropertyName = idPropertyName;
    }

    /// <summary>
    ///     The name of the property on the action or mutation that holds the entity ID, or null when the row is
    ///     read by <see cref="Specification" />.
    /// </summary>
    public string? IdPropertyName { get; }

    /// <summary>
    ///     A static member returning a <c>Specification&lt;TEntity&gt;</c> — <c>nameof(EmployeeSpecifications.ActiveWithNumber)</c>,
    ///     or the bare name of a member of <c>{Entity}Specifications</c> — that reads the row instead of a key.
    /// </summary>
    /// <remarks>
    ///     The member's parameters bind by name, ignoring case, to the operation's properties; an optional one
    ///     with no property of its name keeps its default. Exclusive with the key: one or the other
    ///     (<c>PRAG0456</c>). A member that is not a specification of the entity is <c>PRAG0454</c>, a
    ///     parameter that binds nothing <c>PRAG0455</c>.
    /// </remarks>
    public string? Specification { get; set; }

    /// <summary>
    ///     The entity's <c>[LogicKey]</c> member the key property holds, instead of its id —
    ///     <c>[LoadEntity&lt;Employee&gt;(nameof(EmployeeNumber), By = nameof(Employee.EmployeeNumber))]</c>.
    /// </summary>
    /// <remarks>
    ///     The row is read through the lookup the generator writes for the logic key
    ///     (<c>{Entity}Specifications.GetBy{Key}Async</c>) — the repository's filters and tracking, as by id — and
    ///     a key that names no row is a 404 carrying it. The member must be the entity's single-part logic key
    ///     (<c>PRAG0460</c> otherwise) and the key property of its type (<c>PRAG0461</c>).
    /// </remarks>
    public string? By { get; set; }

    /// <summary>
    ///     Whether the caller must also hold the entity's read permission — the value of its CRUD
    ///     <c>Read</c> constant. Checked before the row is read: 403 without it.
    /// </summary>
    /// <remarks>
    ///     By default a load is authorized by the operation's own permission and by the row filters. An
    ///     internal call — one operation invoking another through its boundary — is not asked, as it is not
    ///     asked the operation's permission either.
    /// </remarks>
    public bool RequireReadPermission { get; set; }

    /// <summary>
    ///     Override the default field name for the loaded entity.
    ///     By default, the field name is derived from the entity type (e.g., _reservation).
    /// </summary>
    public string? FieldName { get; set; }

    /// <summary>
    ///     The navigations loaded with the row — dotted paths, several separated by commas:
    ///     <c>"Members"</c>, <c>"Members, Manager"</c>, <c>"Lines.Product"</c>.
    /// </summary>
    /// <remarks>
    ///     There is no lazy loading: a navigation not named here is empty on the loaded entity, whatever the
    ///     database holds. A path that names no navigation of the entity is <c>PRAG0453</c>.
    /// </remarks>
    public string? Include { get; set; }
}
