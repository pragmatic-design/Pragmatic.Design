namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Specifies the delete behavior for a relationship.
/// </summary>
/// <remarks>
///     <para>
///         This enum is ORM-agnostic. The persistence implementation (e.g., Entity Framework)
///         will map these values to the corresponding ORM-specific behavior. The member names
///         intentionally mirror EF Core's <c>DeleteBehavior</c>, and the source generator emits
///         <c>OnDelete(DeleteBehavior.{Name})</c> by member <em>name</em> (never by numeric value),
///         so the explicit ordinal assignments below only affect what <c>default(DeleteBehavior)</c>
///         resolves to — not the generated configuration code.
///     </para>
///     <para>
///         <see cref="ClientSetNull" /> is assigned the zero value so that
///         <c>default(DeleteBehavior)</c> matches EF Core's own default behavior, rather than
///         silently meaning <see cref="Restrict" />. Explicit ordinals are used so adding or
///         reordering members in the future cannot accidentally shift the zero/default value.
///     </para>
/// </remarks>
public enum DeleteBehavior
{
    /// <summary>
    ///     EF Core's default behavior: the foreign key is set to null for tracked dependents,
    ///     while the database takes no action. This is the value of <c>default(DeleteBehavior)</c>.
    /// </summary>
    ClientSetNull = 0,

    /// <summary>
    ///     The delete operation is not cascaded to dependent entities.
    ///     An exception is thrown if dependent entities exist.
    /// </summary>
    Restrict = 1,

    /// <summary>
    ///     The foreign key is set to null when the principal is deleted.
    ///     Requires the foreign key to be nullable.
    /// </summary>
    SetNull = 2,

    /// <summary>
    ///     Dependent entities are deleted when the principal is deleted.
    /// </summary>
    Cascade = 3,

    /// <summary>
    ///     No action is taken on dependent entities when the principal is deleted.
    ///     The database may throw an exception if referential integrity is violated.
    /// </summary>
    NoAction = 4
}
