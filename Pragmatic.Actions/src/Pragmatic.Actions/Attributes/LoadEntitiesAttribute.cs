namespace Pragmatic.Actions.Attributes;

/// <summary>
///     Marks a DomainAction or a Mutation to load, before execution, the rows named by a list of keys it
///     carries. The generator creates an <c>IReadOnlyList&lt;TEntity&gt;</c> field for them and reads them
///     in the invoker's preparation hook — after authorization — in one query.
/// </summary>
/// <remarks>
///     <para>
///         The rows come in the order of the keys, a key given twice once. A key that names no row is a
///         404 — one <c>NotFoundError</c> naming every missing key, not the first. An empty list, or a null
///         one, is an empty field and no query.
///     </para>
///     <para>
///         The read goes through the entity's repository, as <c>[LoadEntity]</c> does: the same filters,
///         and on a mutation the same unit of work the mutation saves. The key property is a collection of
///         the entity's key type — <c>IReadOnlyList&lt;Guid&gt;</c>, an array, a list — or <c>PRAG0411</c>
///         says so on the attribute.
///     </para>
///     <para>
///         Instead of keys, a named rule: <c>Specification = nameof(EmployeeSpecifications.InTeam)</c> reads
///         every row it matches, its parameters bound by name to the operation's properties. No row is an
///         empty list — or a 404 with <see cref="RequireAny" />.
///     </para>
/// </remarks>
/// <typeparam name="TEntity">The entity type to load.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class LoadEntitiesAttribute<TEntity> : Attribute where TEntity : class
{
    /// <summary>
    ///     Creates a LoadEntities attribute that reads the rows by <see cref="Specification" />.
    /// </summary>
    public LoadEntitiesAttribute()
    {
    }

    /// <summary>
    ///     Creates a new LoadEntities attribute.
    /// </summary>
    /// <param name="idsPropertyName">
    ///     The name of the property on the action or mutation that holds the keys.
    /// </param>
    public LoadEntitiesAttribute(string idsPropertyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(idsPropertyName);
        IdsPropertyName = idsPropertyName;
    }

    /// <summary>
    ///     The name of the property on the action or mutation that holds the keys, or null when the rows are
    ///     read by <see cref="Specification" />.
    /// </summary>
    public string? IdsPropertyName { get; }

    /// <summary>
    ///     A static member returning a <c>Specification&lt;TEntity&gt;</c> — <c>nameof(EmployeeSpecifications.InTeam)</c>,
    ///     or the bare name of a member of <c>{Entity}Specifications</c> — that reads the rows instead of keys.
    /// </summary>
    /// <remarks>
    ///     Bound as <see cref="LoadEntityAttribute{TEntity}.Specification" /> is. Exclusive with the keys
    ///     (<c>PRAG0456</c>).
    /// </remarks>
    public string? Specification { get; set; }

    /// <summary>Whether no row is a 404 rather than an empty list.</summary>
    public bool RequireAny { get; set; }

    /// <summary>
    ///     Whether the caller must also hold the entity's read permission — the value of its CRUD
    ///     <c>Read</c> constant. Checked before the rows are read: 403 without it.
    /// </summary>
    public bool RequireReadPermission { get; set; }

    /// <summary>
    ///     Override the default field name for the loaded rows.
    ///     By default, the field name is the plural of the entity type (e.g., _employees).
    /// </summary>
    public string? FieldName { get; set; }
}
