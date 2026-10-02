namespace Pragmatic.Result.EntityFrameworkCore;

/// <summary>
///     Represents a database constraint violation error.
/// </summary>
/// <remarks>
///     <para>
///         This error is returned when EF Core throws DbUpdateException for constraint
///         violations other than unique constraints (which use <see cref="DbConflictError" />).
///     </para>
///     <para>
///         Common cases include: foreign key violations, check constraint violations,
///         not null violations, etc.
///     </para>
///     <para>
///         Status code 400 (Bad Request).
///     </para>
/// </remarks>
public sealed record DbConstraintError : Error
{
    /// <inheritdoc />
    public override string Code => "DB_CONSTRAINT";

    /// <inheritdoc />
    public override int StatusCode => 400;

    /// <inheritdoc />
    public override string Title => "Database Constraint Violation";

    /// <summary>
    ///     Gets the name of the constraint that was violated.
    /// </summary>
    public string? ConstraintName { get; init; }

    /// <summary>
    ///     Gets the type of constraint (e.g., "ForeignKey", "Check", "NotNull").
    /// </summary>
    public string? ConstraintType { get; init; }

    /// <summary>
    ///     Gets the name of the table where the constraint was violated.
    /// </summary>
    public string? TableName { get; init; }

    /// <summary>
    ///     Gets additional, server-side diagnostic details about the error.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         SECURITY: When populated from a raw database exception (see <see cref="FromDetails" />),
    ///         this MAY contain internal schema/constraint text (table names, column names, raw provider
    ///         messages). It is retained for SERVER-SIDE diagnostics and MUST NOT be exposed to clients
    ///         as-is.
    ///     </para>
    ///     <para>
    ///         The client-facing message is the generic <see cref="Title" /> ("Database Constraint
    ///         Violation"); <see cref="Details" /> must be gated by the host's error middleware before
    ///         client exposure, consistent with <c>InternalServerError.includeDetails</c>.
    ///     </para>
    /// </remarks>
    public string? Details { get; init; }

    /// <summary>
    ///     Creates a foreign key constraint violation error.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="constraintName">The constraint name if available.</param>
    /// <returns>A DbConstraintError for foreign key violation.</returns>
    public static DbConstraintError ForeignKeyViolation(string tableName, string? constraintName = null)
    {
        return new DbConstraintError
        {
            ConstraintType = "ForeignKey",
            ConstraintName = constraintName,
            TableName = tableName,
            Details = "Referenced entity does not exist or cannot be deleted due to existing references."
        };
    }

    /// <summary>
    ///     Creates a generic constraint violation error from a raw database exception message.
    /// </summary>
    /// <param name="details">
    ///     Raw error text from the database exception. SECURITY: this is stored in
    ///     <see cref="Details" /> for SERVER-SIDE diagnostics only and may contain internal
    ///     schema/constraint information. The client-facing message remains the generic
    ///     <see cref="Title" /> ("Database Constraint Violation"); the host's error middleware
    ///     must gate <see cref="Details" /> before any client exposure (consistent with
    ///     <c>InternalServerError.includeDetails</c>).
    /// </param>
    /// <returns>A DbConstraintError carrying the raw detail for diagnostics behind a generic title.</returns>
    public static DbConstraintError FromDetails(string? details)
    {
        return new DbConstraintError { Details = details };
    }
}