namespace Pragmatic.Identity.Persistence;

/// <summary>
///     Thrown when an attempt is made to insert a second <em>active</em>
///     (<c>ValidTo IS NULL</c>) row for a temporal relation whose key already has one.
/// </summary>
/// <remarks>
///     This is the application-layer counterpart of the filtered unique index
///     (<c>UX_*_Active</c>) that enforces the "at most one active row per key" invariant on
///     relational providers. On providers that cannot express a partial index (EF Core InMemory,
///     or no provider configured) the index degrades to a plain, non-unique lookup, and this
///     guard is the only thing standing between the caller and a silent duplicate.
///     See <see cref="TemporalActiveRowExtensions"/>.
/// </remarks>
public sealed class TemporalActiveRowConflictException : InvalidOperationException
{
    /// <summary>Creates the exception with a caller-supplied description of the conflicting key.</summary>
    /// <param name="conflictDescription">
    ///     Human-readable description of the key that already has an active row
    ///     (e.g. <c>"UserRole(user=…, role=admin)"</c>).
    /// </param>
    public TemporalActiveRowConflictException(string conflictDescription)
        : base($"An active row already exists for {conflictDescription}. "
               + "Revoke it (set ValidTo) before inserting a new active row.")
        => ConflictDescription = conflictDescription;

    /// <summary>The description of the key that already has an active row.</summary>
    public string ConflictDescription { get; }
}
