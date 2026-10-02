namespace Pragmatic.Result.EntityFrameworkCore;

/// <summary>
///     Represents a database conflict error such as concurrency conflicts or unique violations.
/// </summary>
/// <remarks>
///     <para>
///         This error is returned when EF Core throws DbUpdateConcurrencyException or
///         when a unique constraint is violated.
///     </para>
///     <para>
///         Status code 409 (Conflict).
///     </para>
/// </remarks>
public sealed record DbConflictError : Error
{
    /// <inheritdoc />
    public override string Code => "DB_CONFLICT";

    /// <inheritdoc />
    public override int StatusCode => 409;

    /// <inheritdoc />
    public override string Title => "Database Conflict";

    /// <summary>The <see cref="Reason" />: what the caller reads as the problem's detail.</summary>
    public override string? Description => Reason;

    /// <summary>
    ///     Gets the type of entity involved in the conflict.
    /// </summary>
    public string? EntityType { get; init; }

    /// <summary>
    ///     Gets the ID of the entity involved in the conflict.
    /// </summary>
    public object? EntityId { get; init; }

    /// <summary>
    ///     Gets the reason for the conflict.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    ///     Gets the name of the field that caused the conflict (for unique violations).
    /// </summary>
    public string? FieldName { get; init; }

    /// <summary>
    ///     The database constraint that was violated, when the provider named one.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <b>Not the same thing as <see cref="FieldName" />, and kept separate for that reason.</b>
    ///     PostgreSQL does not fill a column for SQLSTATE 23505 — it fills the constraint and the table
    ///     — so this is usually the only thing the provider said. An index name is not something a form can mark; turning it into the properties it
    ///     covers needs the model, which is a layer up. Carried here so that layer has something to
    ///     work from, and so a caller that has neither is not left with nothing.
    /// </remarks>
    public string? ConstraintName { get; init; }

    /// <summary>
    ///     Creates a concurrency conflict error.
    /// </summary>
    /// <param name="entityType">The entity type name.</param>
    /// <param name="id">The entity ID.</param>
    /// <returns>A DbConflictError for concurrent modification.</returns>
    public static DbConflictError ConcurrencyConflict(string entityType, object? id = null)
    {
        return new DbConflictError
        {
            EntityType = entityType,
            EntityId = id,
            Reason = "The entity was modified or deleted by another operation."
        };
    }

    /// <summary>
    ///     Creates a unique constraint violation error.
    /// </summary>
    /// <param name="entityType">The entity type name.</param>
    /// <param name="fieldName">The field with duplicate value.</param>
    /// <param name="constraintName">The violated constraint, when the provider named one.</param>
    /// <returns>A DbConflictError for unique violation.</returns>
    /// <remarks>
    ///     The reason says the most specific thing known: the field if there is one, else the
    ///     constraint, else that something was duplicated. ⚠️ On PostgreSQL the field is usually
    ///     absent, so without the constraint the reason would always be the last of those — a 409 a
    ///     client could not act on.
    /// </remarks>
    public static DbConflictError UniqueViolation(
        string entityType, string? fieldName = null, string? constraintName = null)
    {
        return new DbConflictError
        {
            EntityType = entityType,
            FieldName = fieldName,
            ConstraintName = constraintName,
            Reason = fieldName is not null
                ? $"Duplicate value for {fieldName}."
                : constraintName is not null
                    ? $"Duplicate value violates unique constraint {constraintName}."
                    : "Duplicate value violates unique constraint."
        };
    }
}