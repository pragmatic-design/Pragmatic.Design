using System.Collections.Immutable;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Samples.Samples;

/// <summary>
///     Hand-authored "desired schema" snapshots. In a real project, the
///     Pragmatic source generator builds these from your `[Entity]` types at
///     compile time (see the module README for the SG path). Building them
///     manually in this sample keeps the focus on the diff + SQL + execute
///     pipeline, which is identical either way.
/// </summary>
internal static class DesiredSchemas
{
    /// <summary>V1: just the Customers table with 3 columns.</summary>
    public static SchemaVersion V1 => new(
        Tables:
        [
            new TableSchema(
                Name: "Customers",
                SchemaName: null,           // sqlite has no namespaces
                Columns:
                [
                    new ColumnSchema("Id",         "TEXT", IsNullable: false, IsPrimaryKey: true),
                    new ColumnSchema("FirstName",  "TEXT", IsNullable: false, IsPrimaryKey: false),
                    new ColumnSchema("LastName",   "TEXT", IsNullable: false, IsPrimaryKey: false),
                ],
                Indexes: ImmutableArray<IndexSchema>.Empty,
                ForeignKeys: ImmutableArray<ForeignKeySchema>.Empty),
        ],
        ProviderName: "Sqlite");

    /// <summary>
    ///     V2: V1 + Email column (nullable, safe to add) + an index on Email.
    /// </summary>
    public static SchemaVersion V2 => new(
        Tables:
        [
            new TableSchema(
                Name: "Customers",
                SchemaName: null,
                Columns:
                [
                    new ColumnSchema("Id",         "TEXT", IsNullable: false, IsPrimaryKey: true),
                    new ColumnSchema("FirstName",  "TEXT", IsNullable: false, IsPrimaryKey: false),
                    new ColumnSchema("LastName",   "TEXT", IsNullable: false, IsPrimaryKey: false),
                    new ColumnSchema("Email",      "TEXT", IsNullable: true,  IsPrimaryKey: false),
                ],
                Indexes:
                [
                    new IndexSchema(
                        Name: "IX_Customers_Email",
                        Columns: ["Email"],
                        IsUnique: true),
                ],
                ForeignKeys: ImmutableArray<ForeignKeySchema>.Empty),
        ],
        ProviderName: "Sqlite");
}
