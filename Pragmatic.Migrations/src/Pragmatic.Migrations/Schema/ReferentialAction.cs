namespace Pragmatic.Migrations.Schema;

/// <summary>
///     Referential action applied on a foreign key's ON DELETE clause.
///     Mirrors EF Core's DeleteBehavior names so SG-emitted metadata maps 1:1.
/// </summary>
/// <remarks>
///     The names mirror <c>Pragmatic.Persistence.Entity.DeleteBehavior</c>; the <b>numbers do
///     not</b> — there <c>Restrict</c> is 1 and <c>Cascade</c> is 3. Every mapping is by name, so
///     no code depends on the ordinals, but a serialised schema records them as plain integers:
///     <c>"onDelete": 1</c> in a schema file is <c>Cascade</c>, and decoding it with the other enum
///     gives a wrong name that still looks like a valid answer.
/// </remarks>
public enum ReferentialAction
{
    /// <summary>No action — the database rejects the delete if dependents exist (default).</summary>
    NoAction = 0,

    /// <summary>Cascade — dependent rows are deleted with the principal.</summary>
    Cascade = 1,

    /// <summary>Set the referencing column(s) to NULL.</summary>
    SetNull = 2,

    /// <summary>Restrict — equivalent to NoAction on most providers.</summary>
    Restrict = 3
}
