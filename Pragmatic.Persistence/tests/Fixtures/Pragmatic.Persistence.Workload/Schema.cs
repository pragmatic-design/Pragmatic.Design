using Pragmatic.Persistence.Sql;

namespace Pragmatic.Persistence.Workload;

/// <summary>
///     The schema of the workload in each dialect: table and column names as the repository's generator
///     writes them (pluralised type name, property names, quoted). Guids, dates and decimals take the
///     type each database has for them; SQLite has no decimal, so money is REAL there, as EF Core's
///     SQLite provider is told to map it (its default, TEXT, does not compare numerically).
/// </summary>
public static class Schema
{
    public static IReadOnlyList<string> Tables { get; } = ["OrderTags", "Notes", "Events", "OrderLines", "Orders", "Customers"];

    public static string Quote(SqlDialect dialect, string identifier) => dialect == SqlDialect.SqlServer ? "[" + identifier + "]" : "\"" + identifier + "\"";

    public static IReadOnlyList<string> Create(SqlDialect dialect)
    {
        var (guid, text, money, date, boolean, integer) = dialect switch
        {
            SqlDialect.Sqlite => ("TEXT", "TEXT", "REAL", "TEXT", "INTEGER", "INTEGER"),
            SqlDialect.PostgreSql => ("uuid", "text", "numeric(18,2)", "timestamptz", "boolean", "integer"),
            SqlDialect.SqlServer => ("uniqueidentifier", "nvarchar(200)", "decimal(18,2)", "datetimeoffset", "bit", "int"),
            SqlDialect.MySql => ("char(36)", "varchar(200)", "decimal(18,2)", "datetime(6)", "tinyint(1)", "int"), // a DateTimeOffset as the UTC instant; a guid as MySqlConnector's default text form
            SqlDialect.Oracle => ("VARCHAR2(36)", OracleText, "NUMBER(18,2)", "TIMESTAMP(7)", "BOOLEAN", "NUMBER(10)"), // 23ai: BOOLEAN; a guid as text, a DateTimeOffset as the UTC instant
            _ => throw new ArgumentOutOfRangeException(nameof(dialect)),
        };
        string Q(string name) => Quote(dialect, name);
        // Order numbers are codes: on SQL Server a binary collation makes LIKE a byte comparison (the server default,
        // SQL_Latin1_General_CP1_CI_AS, compares linguistically and case-insensitively on every row) and gives Contains
        // the case-sensitive meaning it has on PostgreSQL and SQLite; MySQL's default collation is case-insensitive too.
        var code = dialect switch { SqlDialect.SqlServer => text + " COLLATE Latin1_General_100_BIN2", SqlDialect.MySql => text + " COLLATE utf8mb4_bin", _ => text };
        // A key the database assigns, in each dialect's words (SQLite's AUTOINCREMENT wants exactly INTEGER PRIMARY KEY).
        var identity = dialect switch
        {
            SqlDialect.Sqlite => "INTEGER PRIMARY KEY AUTOINCREMENT",
            SqlDialect.PostgreSql => "bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY",
            SqlDialect.MySql => "bigint AUTO_INCREMENT PRIMARY KEY",
            SqlDialect.Oracle => "NUMBER(19) GENERATED ALWAYS AS IDENTITY PRIMARY KEY",
            _ => "bigint IDENTITY(1,1) PRIMARY KEY",
        };
        return OracleNullableText(dialect,
        [
            $"CREATE TABLE {Q("Customers")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("Name")} {text} NOT NULL, {Q("Email")} {text} NOT NULL, {Q("Country")} {text} NOT NULL, {Q("CreatedAt")} {date} NOT NULL)",
            $"CREATE TABLE {Q("Orders")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("CustomerId")} {guid} NOT NULL REFERENCES {Q("Customers")}({Q("Id")}), {Q("Number")} {code} NOT NULL, {Q("Status")} {integer} NOT NULL, {Q("Total")} {money} NOT NULL, {Q("CreatedAt")} {date} NOT NULL, {Q("IsDeleted")} {boolean} NOT NULL)",
            $"CREATE TABLE {Q("OrderLines")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("OrderId")} {guid} NOT NULL REFERENCES {Q("Orders")}({Q("Id")}), {Q("Sku")} {text} NOT NULL, {Q("Quantity")} {integer} NOT NULL, {Q("UnitPrice")} {money} NOT NULL)",
            $"CREATE TABLE {Q("Events")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("OrderId")} {guid} NOT NULL, {Q("Kind")} {text} NOT NULL, {Q("At")} {date} NOT NULL, {Q("Version")} {integer} NOT NULL)",
            $"CREATE TABLE {Q("Notes")} ({Q("Id")} {identity}, {Q("OrderId")} {guid} NOT NULL, {Q("Text")} {text} NOT NULL)",
            $"CREATE TABLE {Q("OrderTags")} ({Q("OrderId")} {guid} NOT NULL, {Q("Tag")} {code} NOT NULL, {Q("Note")} {text} NOT NULL, {Q("Version")} {integer} NOT NULL, PRIMARY KEY ({Q("OrderId")}, {Q("Tag")}))",
            $"CREATE INDEX {Q("IX_Orders_CustomerId")} ON {Q("Orders")} ({Q("CustomerId")})",
            $"CREATE INDEX {Q("IX_Orders_Number")} ON {Q("Orders")} ({Q("Number")})",
            $"CREATE INDEX {Q("IX_OrderLines_OrderId")} ON {Q("OrderLines")} ({Q("OrderId")})",
        ]);
    }

    /// <summary>Oracle's text type in the workload's tables.</summary>
    public const string OracleText = "VARCHAR2(200 CHAR)";

    /// <summary>
    ///     Oracle stores the empty string as NULL: a NOT NULL text column refuses "" (ORA-01400). The workload writes empty texts, so on Oracle
    ///     its text columns are nullable (the library reads NULL back as ""); the other dialects keep the DDL as written.
    /// </summary>
    public static IReadOnlyList<string> OracleNullableText(SqlDialect dialect, IReadOnlyList<string> statements)
        => dialect != SqlDialect.Oracle ? statements : statements.Select(s => s.Replace(OracleText + " NOT NULL", OracleText, StringComparison.Ordinal).Replace("BLOB NOT NULL", "BLOB", StringComparison.Ordinal)).ToList(); // an empty BLOB is NULL too

    public static IReadOnlyList<string> Drop(SqlDialect dialect)
    {
        // PostgreSQL refuses to drop a table a view or a function depends on (ExtrasSchema's CustomerSummary and orders_of): CASCADE drops them too, ExtrasSchema recreates them.
        var drops = new List<string>(Tables.Count);
        foreach (var table in Tables)
            drops.Add(dialect switch
            {
                SqlDialect.SqlServer => $"IF OBJECT_ID('{table}', 'U') IS NOT NULL DROP TABLE [{table}]",
                SqlDialect.PostgreSql => $"DROP TABLE IF EXISTS \"{table}\" CASCADE",
                SqlDialect.Oracle => $"DROP TABLE IF EXISTS \"{table}\" CASCADE CONSTRAINTS PURGE",
                _ => $"DROP TABLE IF EXISTS \"{table}\"",
            });
        return drops;
    }
}
