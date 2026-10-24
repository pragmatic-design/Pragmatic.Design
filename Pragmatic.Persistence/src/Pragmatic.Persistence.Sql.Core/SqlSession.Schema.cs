using System.Data.Common;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     Schema drift: the generated model (every registered map: table, columns, kinds) against what the database has
///     (<c>information_schema.columns</c> on PostgreSQL and SQL Server, <c>PRAGMA table_xinfo</c> on SQLite, which lists the generated columns too). A table or a column
///     the model reads and the database lacks, a column stored as a type the kind cannot read, a column nobody maps: each is a
///     <see cref="SqlSchemaProblem" /> with the names and the types, for a startup check or a deployment gate. Nullability and
///     lengths are not compared (the model does not carry them).
/// </summary>
public sealed partial class SqlSession
{
    public async Task<SqlSchemaReport> VerifySchemaAsync(CancellationToken ct = default)
    {
        var problems = new List<SqlSchemaProblem>();
        // Several entities may map one table (a hierarchy's types, an entity reading a subset): the table is read once, a column is unmapped when none of them has it.
        var tables = SqlEntityMap.All.GroupBy(m => m.Table, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        foreach (var table in tables)
        {
            var actual = await ColumnsOfAsync(table.Key, ct).ConfigureAwait(false);
            if (actual.Count == 0)
            {
                problems.Add(new SqlSchemaProblem(SqlSchemaProblemKind.MissingTable, table.Key, null, null, null));
                continue;
            }

            var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var map in table)
            {
                foreach (var column in map.Columns)
                {
                    mapped.Add(column.Column);
                    if (!reported.Add(column.Column))
                        continue; // the same column through another entity of the table: one report
                    var expected = Expected(column.Kind, Dialect);
                    if (!actual.TryGetValue(column.Column, out var type))
                        problems.Add(new SqlSchemaProblem(SqlSchemaProblemKind.MissingColumn, table.Key, column.Column, expected, null));
                    else if (!Accepts(column.Kind, type, Dialect))
                        problems.Add(new SqlSchemaProblem(SqlSchemaProblemKind.TypeMismatch, table.Key, column.Column, expected, type));
                }
            }

            foreach (var pair in actual)
            {
                if (!mapped.Contains(pair.Key))
                    problems.Add(new SqlSchemaProblem(SqlSchemaProblemKind.UnmappedColumn, table.Key, pair.Key, null, pair.Value));
            }
        }

        return new SqlSchemaReport(problems, tables.Count);
    }

    /// <summary>The table's columns and their declared types, as the catalog names them; empty when the table is not there.</summary>
    private async Task<Dictionary<string, string>> ColumnsOfAsync(string table, CancellationToken ct)
    {
        // The session's schema when it has one (a tenant's): the attached database's PRAGMA on SQLite, table_schema in the catalog elsewhere.
        var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var command = Connection.CreateCommand();
        command.Transaction = Transaction;
        if (Dialect == SqlDialect.Sqlite)
            command.CommandText = $"PRAGMA {(Schema is null ? "" : "\"" + Schema.Replace("\"", "\"\"") + "\".")}table_xinfo(\"{table.Replace("\"", "\"\"")}\")";
        else
        {
            // MySQL's information_schema spans every database of the server: the connection's own when the session names no schema. Oracle has its own catalog (the owner is the schema, the connected user by default).
            command.CommandText = Dialect == SqlDialect.Oracle
                ? "SELECT column_name, data_type FROM all_tab_columns WHERE table_name = @p0 AND owner = " + (Schema is not null ? "@p1" : "USER")
                : "SELECT column_name, data_type FROM information_schema.columns WHERE table_name = @p0" + (Schema is not null ? " AND table_schema = @p1" : Dialect == SqlDialect.MySql ? " AND table_schema = DATABASE()" : "");
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@p0";
            parameter.Value = table;
            command.Parameters.Add(parameter);
            if (Schema is not null)
            {
                var schema = command.CreateParameter();
                schema.ParameterName = "@p1";
                schema.Value = Schema;
                command.Parameters.Add(schema);
            }

            if (Dialect == SqlDialect.Oracle)
                SqlOracle.Adapt(command);
        }

        using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var name = Dialect == SqlDialect.Sqlite ? 1 : 0; // PRAGMA table_xinfo: cid, name, type, …
        var type = Dialect == SqlDialect.Sqlite ? 2 : 1;
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            columns[reader.GetString(name)] = reader.GetString(type);
        return columns;
    }

    /// <summary>The type a kind is stored as on the dialect, as the catalog names it (the first of the accepted ones), for the report.</summary>
    private static string Expected(SqlParamKind kind, SqlDialect dialect) => Accepted(kind, dialect)[0];

    private static bool Accepts(SqlParamKind kind, string actual, SqlDialect dialect)
    {
        // SQLite declares what the CREATE TABLE said (a type affinity, in any case) and nothing for a view's computed column: nothing to compare there; the others name the catalog type.
        var type = actual.Trim().ToLowerInvariant();
        if (type.Length == 0 && dialect == SqlDialect.Sqlite)
            return true;
        var paren = type.IndexOf('(');
        if (paren > 0)
            type = type.Substring(0, paren);
        return Accepted(kind, dialect).Contains(type);
    }

    /// <summary>What each kind reads from, per dialect: the schema's types for it and the ones its reader takes as well.</summary>
    private static string[] Accepted(SqlParamKind kind, SqlDialect dialect) => (kind, dialect) switch
    {
        // Oracle: guids, days and times of day as text; integers, decimals and longs as NUMBER; instants as TIMESTAMP (UTC).
        (SqlParamKind.Guid or SqlParamKind.String or SqlParamKind.DateOnly or SqlParamKind.TimeOnly, SqlDialect.Oracle) => ["varchar2", "nvarchar2", "char", "nchar", "clob", "nclob", "json"],
        (SqlParamKind.Int32 or SqlParamKind.Int64 or SqlParamKind.Decimal, SqlDialect.Oracle) => ["number", "integer", "float"],
        (SqlParamKind.Double, SqlDialect.Oracle) => ["binary_double", "binary_float", "number", "float"],
        (SqlParamKind.Boolean, SqlDialect.Oracle) => ["boolean", "number"],
        (SqlParamKind.DateTimeOffset or SqlParamKind.DateTime, SqlDialect.Oracle) => ["timestamp", "date"],
        (SqlParamKind.Bytes, SqlDialect.Oracle) => ["blob", "raw"],
        (SqlParamKind.Vector, SqlDialect.Oracle) => ["vector", "varchar2", "clob"],
        (SqlParamKind.Guid, SqlDialect.Sqlite) => ["text"],
        (SqlParamKind.Guid, SqlDialect.PostgreSql) => ["uuid"],
        (SqlParamKind.Guid, SqlDialect.MySql) => ["char"],
        (SqlParamKind.Guid, _) => ["uniqueidentifier"],
        (SqlParamKind.String, SqlDialect.Sqlite) => ["text"],
        (SqlParamKind.String, SqlDialect.PostgreSql) => ["text", "character varying", "character", "jsonb", "json"],
        (SqlParamKind.String, SqlDialect.MySql) => ["varchar", "text", "char", "longtext", "mediumtext", "tinytext", "json"],
        (SqlParamKind.String, _) => ["nvarchar", "varchar", "nchar", "char", "ntext", "text"],
        (SqlParamKind.Int32, SqlDialect.Sqlite) => ["integer", "int"],
        (SqlParamKind.Int32, SqlDialect.PostgreSql) => ["integer", "smallint"],
        (SqlParamKind.Int32, SqlDialect.MySql) => ["int", "bigint", "smallint", "mediumint", "tinyint"], // COUNT and the integer casts are BIGINT on MySQL (a view's column): the reader converts
        (SqlParamKind.Int32, _) => ["int", "smallint", "tinyint"],
        (SqlParamKind.Int64, SqlDialect.Sqlite) => ["integer", "bigint"],
        (SqlParamKind.Int64, _) => ["bigint"],
        (SqlParamKind.Decimal, SqlDialect.Sqlite) => ["real", "numeric", "decimal"],
        (SqlParamKind.Decimal, SqlDialect.PostgreSql) => ["numeric"],
        (SqlParamKind.Decimal, _) => ["decimal", "numeric", "money"],
        (SqlParamKind.Double, SqlDialect.Sqlite) => ["real"],
        (SqlParamKind.Double, SqlDialect.PostgreSql) => ["double precision", "real"],
        (SqlParamKind.Double, SqlDialect.MySql) => ["double", "float"],
        (SqlParamKind.Double, _) => ["float", "real"],
        (SqlParamKind.Boolean, SqlDialect.Sqlite) => ["integer", "boolean"],
        (SqlParamKind.Boolean, SqlDialect.PostgreSql) => ["boolean"],
        (SqlParamKind.Boolean, SqlDialect.MySql) => ["tinyint", "bit"],
        (SqlParamKind.Boolean, _) => ["bit"],
        (SqlParamKind.DateTimeOffset, SqlDialect.Sqlite) => ["text"],
        (SqlParamKind.DateTimeOffset, SqlDialect.PostgreSql) => ["timestamp with time zone"],
        (SqlParamKind.DateTimeOffset, SqlDialect.MySql) => ["datetime", "timestamp"], // the UTC instant
        (SqlParamKind.DateTimeOffset, _) => ["datetimeoffset"],
        (SqlParamKind.DateTime, SqlDialect.Sqlite) => ["text"],
        (SqlParamKind.DateTime, SqlDialect.PostgreSql) => ["timestamp without time zone", "timestamp with time zone"],
        (SqlParamKind.DateTime, SqlDialect.MySql) => ["datetime", "timestamp"],
        (SqlParamKind.DateTime, _) => ["datetime2", "datetime", "smalldatetime"],
        (SqlParamKind.Bytes, SqlDialect.Sqlite) => ["blob"],
        (SqlParamKind.Bytes, SqlDialect.PostgreSql) => ["bytea"],
        (SqlParamKind.Bytes, SqlDialect.MySql) => ["blob", "longblob", "mediumblob", "varbinary", "binary"],
        (SqlParamKind.Bytes, _) => ["varbinary", "binary", "image"],
        (SqlParamKind.DateOnly, SqlDialect.Sqlite) => ["text"],
        (SqlParamKind.DateOnly, _) => ["date"],
        (SqlParamKind.TimeOnly, SqlDialect.Sqlite) => ["text"],
        (SqlParamKind.TimeOnly, SqlDialect.PostgreSql) => ["time without time zone"],
        (SqlParamKind.TimeOnly, _) => ["time"],
        (SqlParamKind.Vector, SqlDialect.Sqlite) => ["text"],
        (SqlParamKind.Vector, SqlDialect.PostgreSql) => ["user-defined", "text"], // pgvector's type, as information_schema names an extension type; text where the server has no pgvector
        (SqlParamKind.Vector, SqlDialect.MySql) => ["text", "varchar", "longtext"], // no vector type: the JSON text
        (SqlParamKind.Vector, _) => ["vector", "nvarchar", "varchar"], // SQL Server 2025's type; text before it
        (SqlParamKind.Array, SqlDialect.PostgreSql) => ["array"], // text[], uuid[], integer[]: information_schema says ARRAY
        (SqlParamKind.Array, SqlDialect.Sqlite) => ["text"], // the JSON text
        (SqlParamKind.Array, SqlDialect.MySql) => ["json", "text", "longtext", "varchar"],
        (SqlParamKind.Array, SqlDialect.Oracle) => ["varchar2", "clob", "json"],
        (SqlParamKind.Array, _) => ["nvarchar", "varchar"],
        _ => ["?"],
    };
}
