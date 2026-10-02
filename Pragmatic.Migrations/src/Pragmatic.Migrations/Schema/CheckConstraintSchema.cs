namespace Pragmatic.Migrations.Schema;

/// <summary>
///     A row-level invariant the database enforces, expressed as SQL.
/// </summary>
/// <remarks>
///     <para>
///         The counterpart to <see cref="IndexSchema" /> for rules that constrain one row rather than
///         the relationship between rows. A unique index says "no two rows may agree here"; a check says
///         "no row may look like this" — and unlike a unique index it is evaluated per row as it is
///         written, so it is insensitive to the order in which a unit of work happens to emit its
///         statements.
///     </para>
///     <para>
///         ⚠️ That difference is why this type exists. <c>[TemporalRelation(MaxActive = 1)]</c> became a
///         partial unique index, which holds — but a succession that closes one stretch and opens
///         another in the same <c>SaveChanges</c> trips it, because EF emits the INSERT before the
///         UPDATE and PostgreSQL cannot defer a <em>partial</em> unique index (only constraints defer,
///         and constraints take no WHERE clause). An interval whose end precedes its own start is a
///         different kind of rule, and this is the kind that can state it.
///     </para>
/// </remarks>
/// <param name="Name">Constraint name (e.g. "CK_StaffAssignments_ValidRange").</param>
/// <param name="Expression">
///     The boolean SQL the row must satisfy, already quoted for the target provider.
/// </param>
public sealed record CheckConstraintSchema(string Name, string Expression);
