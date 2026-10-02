using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Diff.Changes;

/// <summary>Add a check constraint to a table.</summary>
/// <remarks>
///     ⚠️ <b>Adding one can fail on data that already violates it</b>, and that is the point rather than
///     a flaw: those rows are what the missing invariant allowed in. The migration stops and names the
///     constraint, which is the only moment anyone will look at them.
/// </remarks>
public sealed record AddCheckConstraint(string TableName, CheckConstraintSchema Check, string? SchemaName = null)
    : SchemaChange($"ADD CHECK {Check.Name} ON {TableName}", IsBreaking: false);

/// <summary>Drop an existing check constraint.</summary>
public sealed record DropCheckConstraint(string TableName, string CheckName, string? SchemaName = null)
    : SchemaChange($"DROP CHECK {CheckName} ON {TableName}", IsBreaking: false);
