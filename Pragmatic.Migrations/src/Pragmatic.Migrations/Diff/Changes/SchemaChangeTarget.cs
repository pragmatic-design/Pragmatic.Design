namespace Pragmatic.Migrations.Diff.Changes;

/// <summary>
///     Resolves the table a <see cref="SchemaChange" /> targets. The change records carry the
///     table name under different positional names, so callers that need to group changes by
///     table (the SQLite table-rebuild consolidation, for one) go through here instead of
///     repeating the pattern match.
/// </summary>
public static class SchemaChangeTarget
{
    /// <summary>
    ///     Returns the name of the table the change applies to, or null for changes that do not
    ///     target a single table.
    /// </summary>
    public static string? TableNameOf(SchemaChange change) => change switch
    {
        CreateTable c => c.Table.Name,
        DropTable c => c.TableName,
        AddColumn c => c.TableName,
        DropColumn c => c.TableName,
        RenameColumn c => c.TableName,
        AlterColumnType c => c.TableName,
        AlterColumnNullability c => c.TableName,
        AlterColumnDefault c => c.TableName,
        AddIndex c => c.TableName,
        DropIndex c => c.TableName,
        AddForeignKey c => c.TableName,
        DropForeignKey c => c.TableName,
        AddCheckConstraint c => c.TableName,
        DropCheckConstraint c => c.TableName,
        AlterPrimaryKey c => c.TableName,
        _ => null
    };
}
