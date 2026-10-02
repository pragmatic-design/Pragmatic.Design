namespace Pragmatic.Migrations.Diff.Changes;

/// <summary>
///     Base record for all schema changes detected by the diff engine.
/// </summary>
/// <param name="Description">Human-readable description of the change.</param>
/// <param name="IsBreaking">Whether this change may cause data loss or application failure.</param>
public abstract record SchemaChange(string Description, bool IsBreaking);
