namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>
///     A database whose schema the generator emits for Pragmatic.Migrations, on a <c>partial</c> class of the application: it gets
///     <c>public static SchemaVersion Schema(SqlDialect dialect)</c> (PostgreSQL, SQL Server, SQLite: the providers Migrations has), the tables of
///     the assembly's entities stored in it (<see cref="StoredInAttribute{TDatabase}" />, every entity when the assembly declares one database)
///     with their columns, keys, indexes, foreign keys and the join tables of the many-to-many sides. Emitted only when the compilation
///     references Pragmatic.Migrations. What Migrations does not describe (the views of a hierarchy, the history and full-text DDL, the
///     sequences) stays the text the generator and <c>SqlTemporal</c>, <c>SqlFullText</c>, <c>SqlSequences</c> give, run after the migration.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SqlDatabaseAttribute : Attribute
{
    /// <summary>The connection string's configuration key (<c>ConnectionStrings:App</c>), carried by the schema for the tools that read it.</summary>
    public string? ConfigKey { get; init; }
}
