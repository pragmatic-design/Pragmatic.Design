using System.Data.Common;
using Pragmatic.Persistence.Sql;

namespace Pragmatic.Persistence.Workload;

/// <summary>The tables of the entities in Extras.cs, in each dialect: dropped and created empty, after the workload's schema, by the tests and the native smoke.</summary>
public static class ExtrasSchema
{
    public static IReadOnlyList<string> Tables { get; } = ["DossierNotes", "Halls", "Venues", "Dossiers", "OrderTagLabels", "Memberships", "CartLines", "Carts", "Prices", "Embeddings", "TypedRows", "Shipments", "Charges", "Categories", "Documents", "Reminders", "Assignments", "Tickets", "InvoiceLines", "Invoices", "CardPayments", "WirePayments", "ProjectLabels", "Projects", "Labels", "Gauges", "TagNotes", "SportsCars", "Cars", "Trucks", "Vehicles", "Bundles"];

    /// <summary>The sequence the tests number tickets with (<c>session.NextValueAsync</c>); created by <see cref="SqlSequences.CreateSql" />, dropped first so each run starts over.</summary>
    public const string TicketNumbers = "ticket_numbers";

    public static async Task CreateAsync(DbConnection connection, SqlDialect dialect)
    {
        // The TPC view first among the drops (it reads the tables), last among the creates; its text is the generator's. The sequence likewise.
        // The vector column takes the server's vector type where it has one (pgvector installed, SQL Server 2025): the extension before the table, the
        // HNSW index after (PostgreSQL only: SQL Server 2025's vector index is a preview, SQLite has none); text otherwise, where the samples do not use it.
        var vectors = await HasVectorsAsync(connection, dialect);
        var fullText = await HasFullTextAsync(connection, dialect);
        var statements = new[] { PaymentView.DropSql(dialect), VehicleView.DropSql(dialect), SqlSequences.DropSqlIfThere(dialect, TicketNumbers) }
            .Concat(Drop(dialect)).Concat(vectors && dialect == SqlDialect.PostgreSql ? ["CREATE EXTENSION IF NOT EXISTS vector"] : []).Concat(Create(dialect, vectors)).Concat(ViewsAndFunctions(dialect))
            .Concat(vectors && dialect == SqlDialect.PostgreSql ? ["CREATE INDEX \"IX_Embeddings_Vector\" ON \"Embeddings\" USING hnsw (\"Vector\" vector_cosine_ops)"] : [])
            .Append(PaymentView.CreateSql(dialect)).Append(VehicleView.CreateSql(dialect)).Concat(SqlSequences.CreateSql(dialect, TicketNumbers, start: 1000))
            .Concat(SqlTemporal.CreateSql<Price>(dialect)) // PostgreSQL and SQLite: the history table and its triggers
            .Concat(fullText ? SqlFullText.CreateSql<Ticket>(dialect) : []); // SQLite's FTS5 table, SQL Server's full-text index on Tickets.Title
        foreach (var statement in statements)
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    ///     False on a SQL Server without the Full-Text component (the official image: the samples' and the AOT smoke's container) or in a system database
    ///     (master refuses full-text search), and on an Oracle without Oracle Text (CTXSYS): Tickets.Title then has no index, and Sql.FullText on it is
    ///     the server's error. True elsewhere.
    /// </summary>
    public static async Task<bool> HasFullTextAsync(DbConnection connection, SqlDialect dialect)
    {
        if (dialect is not (SqlDialect.SqlServer or SqlDialect.Oracle))
            return true;
        using var command = connection.CreateCommand();
        command.CommandText = dialect == SqlDialect.Oracle
            ? "SELECT COUNT(*) FROM all_users WHERE username = 'CTXSYS'"
            : "SELECT CASE WHEN FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') = 1 AND DB_NAME() NOT IN ('master', 'tempdb', 'model', 'msdb') THEN 1 ELSE 0 END";
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    /// <summary>True when the server has a vector type: pgvector among the available extensions, SQL Server 2025 or later; SQLite never (text).</summary>
    public static async Task<bool> HasVectorsAsync(DbConnection connection, SqlDialect dialect)
    {
        if (dialect is SqlDialect.Sqlite or SqlDialect.MySql)
            return false;
        if (dialect == SqlDialect.Oracle)
            return true; // 23ai: the VECTOR type is in the kernel
        using var command = connection.CreateCommand();
        command.CommandText = dialect == SqlDialect.PostgreSql
            ? "SELECT COUNT(*) FROM pg_available_extensions WHERE name = 'vector'"
            : "SELECT CASE WHEN CAST(SERVERPROPERTY('ProductMajorVersion') AS int) >= 17 THEN 1 ELSE 0 END";
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    /// <summary>
    ///     The view <c>CustomerSummary</c> over the workload's tables (the order count as a 32-bit integer on every dialect), and the
    ///     table-valued function <c>orders_of(customer)</c> where the dialect has them (PostgreSQL, SQL Server under <c>dbo</c>; SQLite
    ///     defines no functions in SQL). Each statement alone: SQL Server wants CREATE VIEW / FUNCTION first in its batch.
    /// </summary>
    private static IReadOnlyList<string> ViewsAndFunctions(SqlDialect dialect)
    {
        string Q(string name) => Schema.Quote(dialect, name);
        var count = dialect switch { SqlDialect.PostgreSql => $"CAST(COUNT(o.{Q("Id")}) AS integer)", SqlDialect.MySql => $"CAST(COUNT(o.{Q("Id")}) AS SIGNED)", SqlDialect.Oracle => $"CAST(COUNT(o.{Q("Id")}) AS NUMBER(10))", _ => $"COUNT(o.{Q("Id")})" };
        var view = $"SELECT c.{Q("Id")}, c.{Q("Name")}, c.{Q("Country")}, {count} AS {Q("Orders")} FROM {Q("Customers")} c LEFT JOIN {Q("Orders")} o ON o.{Q("CustomerId")} = c.{Q("Id")} GROUP BY c.{Q("Id")}, c.{Q("Name")}, c.{Q("Country")}";
        return dialect switch
        {
            SqlDialect.Sqlite => [$"DROP VIEW IF EXISTS {Q("CustomerSummary")}", $"CREATE VIEW {Q("CustomerSummary")} AS {view}"],
            SqlDialect.MySql => [$"CREATE OR REPLACE VIEW {Q("CustomerSummary")} AS {view}"], // no table-valued functions in MySQL
            SqlDialect.Oracle =>
            [
                $"CREATE OR REPLACE VIEW {Q("CustomerSummary")} AS {view}",
                // A SQL table macro (23ai): called in FROM as orders_of(:p0), its text expanded with the argument in place.
                $"CREATE OR REPLACE FUNCTION orders_of(customer VARCHAR2) RETURN VARCHAR2 SQL_MACRO(TABLE) IS BEGIN RETURN 'SELECT * FROM {Q("Orders")} WHERE {Q("CustomerId")} = customer'; END;",
            ],
            SqlDialect.PostgreSql =>
            [
                $"CREATE OR REPLACE VIEW {Q("CustomerSummary")} AS {view}",
                $"CREATE OR REPLACE FUNCTION orders_of(customer uuid) RETURNS SETOF {Q("Orders")} LANGUAGE sql STABLE AS $$ SELECT * FROM {Q("Orders")} WHERE {Q("CustomerId")} = customer $$",
            ],
            _ =>
            [
                $"CREATE OR ALTER VIEW {Q("CustomerSummary")} AS {view}",
                $"CREATE OR ALTER FUNCTION dbo.orders_of(@customer uniqueidentifier) RETURNS TABLE AS RETURN (SELECT * FROM {Q("Orders")} WHERE {Q("CustomerId")} = @customer)",
            ],
        };
    }

    private static IReadOnlyList<string> Create(SqlDialect dialect, bool vectors)
    {
        string Q(string name) => Schema.Quote(dialect, name);
        var (guid, text, stamp, blob, day, clock, big) = dialect switch
        {
            SqlDialect.Sqlite => ("TEXT", "TEXT", "TEXT", "BLOB", "TEXT", "TEXT", "INTEGER"),
            SqlDialect.PostgreSql => ("uuid", "text", "timestamp", "bytea", "date", "time", "bigint"),
            SqlDialect.SqlServer => ("uniqueidentifier", "nvarchar(200)", "datetime2", "varbinary(max)", "date", "time", "bigint"),
            SqlDialect.MySql => ("char(36)", "varchar(200)", "datetime(6)", "blob", "date", "time(6)", "bigint"),
            SqlDialect.Oracle => ("VARCHAR2(36)", Schema.OracleText, "TIMESTAMP(7)", "BLOB", "VARCHAR2(10)", "VARCHAR2(16)", "NUMBER(19)"), // days and times of day as text, as SQLite
            _ => throw new ArgumentOutOfRangeException(nameof(dialect)),
        };
        var money = dialect switch { SqlDialect.Sqlite => "REAL", SqlDialect.PostgreSql => "numeric(18,2)", SqlDialect.Oracle => "NUMBER(18,2)", _ => "decimal(18,2)" };
        var json = dialect switch { SqlDialect.Sqlite => "TEXT", SqlDialect.PostgreSql => "jsonb", SqlDialect.MySql => "json", SqlDialect.Oracle => "VARCHAR2(4000)", _ => "nvarchar(max)" };
        var integer = dialect switch { SqlDialect.SqlServer or SqlDialect.MySql => "int", SqlDialect.PostgreSql => "integer", SqlDialect.Oracle => "NUMBER(10)", _ => "INTEGER" };
        var flag = dialect switch { SqlDialect.PostgreSql => "boolean", SqlDialect.SqlServer => "bit", SqlDialect.MySql => "tinyint(1)", SqlDialect.Oracle => "BOOLEAN", _ => "INTEGER" }; // as the workload's schema stores a bool
        var arrays = dialect switch { SqlDialect.SqlServer => "nvarchar(max)", SqlDialect.MySql => "json", SqlDialect.Oracle => "VARCHAR2(4000)", _ => "TEXT" }; // an array column's JSON text (PostgreSQL: native arrays)
        var when = dialect switch { SqlDialect.Sqlite => "TEXT", SqlDialect.PostgreSql => "timestamptz", SqlDialect.MySql => "datetime(6)", SqlDialect.Oracle => "TIMESTAMP(7)", _ => "datetimeoffset" }; // a DateTimeOffset, as the workload's schema stores it
        var vector = !vectors ? text : dialect switch { SqlDialect.PostgreSql => $"vector({Embedding.Dimensions})", SqlDialect.Oracle => $"VECTOR({Embedding.Dimensions}, FLOAT32)", _ => $"VECTOR({Embedding.Dimensions})" };
        // Prices: a system-versioned table on SQL Server (the period columns hidden: not in SELECT *, not mapped), a plain table elsewhere.
        var prices = dialect == SqlDialect.SqlServer
            ? $"CREATE TABLE {Q("Prices")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("Sku")} {text} NOT NULL, {Q("Amount")} {money} NOT NULL, {Q("ValidFrom")} datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL, {Q("ValidTo")} datetime2 GENERATED ALWAYS AS ROW END HIDDEN NOT NULL, PERIOD FOR SYSTEM_TIME ({Q("ValidFrom")}, {Q("ValidTo")})) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.{Q("PricesHistory")}))"
            : $"CREATE TABLE {Q("Prices")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("Sku")} {text} NOT NULL, {Q("Amount")} {money} NOT NULL)"
              // Oracle defers a table's segment to its first insert, an operation without undo: a flashback read (AsOf) to an instant the 3 s
              // SCN grain places before that insert meets blocks it cannot roll back (ORA-08176). The segment created with the table instead.
              + (dialect == SqlDialect.Oracle ? " SEGMENT CREATION IMMEDIATE" : "");
        return Schema.OracleNullableText(dialect,
        [
            prices,
            $"CREATE TABLE {Q("Embeddings")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("Label")} {text} NOT NULL, {Q("Vector")} {vector} NOT NULL)",
            $"CREATE TABLE {Q("TypedRows")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("When")} {stamp} NOT NULL, {Q("Blob")} {blob} NOT NULL, {Q("Day")} {day} NOT NULL, {Q("Clock")} {clock} NOT NULL, {Q("Span")} {big} NOT NULL, {Q("Stamp")} {big} NOT NULL, {Q("Status")} {text} NOT NULL, {Q("MaybeWhen")} {stamp} NULL, {Q("MaybeStamp")} {big} NULL)",
            $"CREATE TABLE {Q("Shipments")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("Code")} {text} NOT NULL, {Q("Destination_Street")} {text} NOT NULL, {Q("Destination_City")} {text} NOT NULL, {Q("Destination_Floor")} {integer} NOT NULL, {Q("Return_Street")} {text} NULL, {Q("Return_City")} {text} NULL, {Q("Return_Floor")} {integer} NULL)",
            $"CREATE TABLE {Q("Charges")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("Amount")} {money} NOT NULL, {Q("Description")} {text} NOT NULL, {Q("Kind")} {text} NOT NULL, {Q("Last4")} {text} NULL, {Q("Iban")} {text} NULL, {Q("Days")} {integer} NULL, {Q("Tier")} {integer} NULL)",
            $"CREATE TABLE {Q("Categories")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("ParentId")} {guid} NULL, {Q("Name")} {text} NOT NULL)",
            $"CREATE TABLE {Q("Documents")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("Name")} {text} NOT NULL, {Q("Spec")} {json} NOT NULL)",
            $"CREATE TABLE {Q("Assignments")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("ParentId")} {guid} NOT NULL, {Q("ParentType")} {text} NOT NULL, {Q("Note")} {text} NOT NULL, {Q("Kind")} {text} NOT NULL, {Q("Role")} {text} NULL, {Q("Floor")} {integer} NULL)",
            $"CREATE TABLE {Q("Reminders")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("AssignmentId")} {guid} NOT NULL, {Q("Text")} {text} NOT NULL)",
            $"CREATE TABLE {Q("Tickets")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("ProjectId")} {guid} NOT NULL, {Q("Title")} {text} NOT NULL, {Q("Description")} {text} NOT NULL, {Q("Amount")} {money} NOT NULL, {Q("Priority")} {integer} NOT NULL, {Q("CreatedAt")} {when} NOT NULL)",
            $"CREATE TABLE {Q("Invoices")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("Number")} {text} NOT NULL, {Q("Total")} {money} NOT NULL, {Q("Version")} {integer} NOT NULL, {Q("Active_From")} {day} NULL, {Q("Active_To")} {day} NULL)",
            $"CREATE TABLE {Q("InvoiceLines")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("InvoiceId")} {guid} NOT NULL, {Q("Sku")} {text} NOT NULL, {Q("Quantity")} {integer} NOT NULL)",
            // [RollUp] aggregates: kept by the lines' writes, a default for the cart's insert, which leaves them out.
            $"CREATE TABLE {Q("Carts")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("Name")} {text} NOT NULL, {Q("Units")} {integer} DEFAULT 0 NOT NULL, {Q("LineCount")} {integer} DEFAULT 0 NOT NULL, {Q("Amount")} {money} DEFAULT 0 NOT NULL)",
            $"CREATE TABLE {Q("CartLines")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("CartId")} {guid} NOT NULL, {Q("Quantity")} {integer} NOT NULL, {Q("Price")} {money} NOT NULL)",
            // A relation with a validity period and a soft delete: the tests' rules on the database's clock.
            $"CREATE TABLE {Q("Memberships")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("Member")} {text} NOT NULL, {Q("ValidFrom")} {stamp} NOT NULL, {Q("ValidTo")} {stamp} NULL, {Q("IsDeleted")} {flag} NOT NULL)",
            $"CREATE TABLE {Q("CardPayments")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("OrderId")} {guid} NOT NULL, {Q("Amount")} {money} NOT NULL, {Q("PaidAt")} {stamp} NOT NULL, {Q("Last4")} {text} NOT NULL, {Q("Brand")} {text} NOT NULL)",
            $"CREATE TABLE {Q("WirePayments")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("OrderId")} {guid} NOT NULL, {Q("Amount")} {money} NOT NULL, {Q("PaidAt")} {stamp} NOT NULL, {Q("Iban")} {text} NOT NULL, {Q("Days")} {integer} NOT NULL)",
            $"CREATE TABLE {Q("Labels")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("Name")} {text} NOT NULL)",
            $"CREATE TABLE {Q("Projects")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("Name")} {text} NOT NULL)",
            $"CREATE TABLE {Q("ProjectLabels")} ({Q("ProjectId")} {guid} NOT NULL, {Q("LabelId")} {guid} NOT NULL, PRIMARY KEY ({Q("ProjectId")}, {Q("LabelId")}))",
            // A join table with a composite side: an order tag's (OrderId, Tag) and a label's key.
            $"CREATE TABLE {Q("OrderTagLabels")} ({Q("OrderId")} {guid} NOT NULL, {Q("Tag")} {text}{dialect switch { SqlDialect.SqlServer => " COLLATE Latin1_General_100_BIN2", SqlDialect.MySql => " COLLATE utf8mb4_bin", _ => "" }} NOT NULL, {Q("LabelId")} {guid} NOT NULL, PRIMARY KEY ({Q("OrderId")}, {Q("Tag")}, {Q("LabelId")}))",
            // A computed column (stored where the dialect wants it said) and a default: the columns [DatabaseGenerated] reads back.
            $"CREATE TABLE {Q("Gauges")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("Quantity")} {integer} NOT NULL, {Q("Doubled")} {dialect switch { SqlDialect.SqlServer => "AS ([Quantity] * 2)", SqlDialect.Oracle => $"{integer} GENERATED ALWAYS AS ({Q("Quantity")} * 2) VIRTUAL", _ => $"{integer} GENERATED ALWAYS AS ({Q("Quantity")} * 2) STORED" }}, {Q("Code")} {text} DEFAULT 'NEW' NOT NULL)",
            // Array columns: native arrays on PostgreSQL, their JSON text elsewhere.
            dialect == SqlDialect.PostgreSql
                ? $"CREATE TABLE {Q("Bundles")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("Labels")} text[] NOT NULL, {Q("Refs")} uuid[] NOT NULL, {Q("Scores")} integer[] NOT NULL)"
                : $"CREATE TABLE {Q("Bundles")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("Labels")} {arrays} NOT NULL, {Q("Refs")} {arrays} NOT NULL, {Q("Scores")} {arrays} NOT NULL)",
            // A tenant's rows, owned and shared through access scopes (an array): the visibility rules on the request's context.
            $"CREATE TABLE {Q("DossierNotes")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("DossierId")} {guid} NOT NULL, {Q("Text")} {text} NOT NULL)",
            $"CREATE TABLE {Q("Venues")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("Name")} {text} NOT NULL, {Q("HallCount")} {integer} DEFAULT 0 NOT NULL, {Q("IsDeleted")} {flag} NOT NULL, {Q("DeletedAt")} {when} NULL, {Q("DeletedBy")} {text} NULL, {Q("CreatedAt")} {when} NOT NULL, {Q("CreatedBy")} {text} NULL, {Q("UpdatedAt")} {when} NULL, {Q("UpdatedBy")} {text} NULL, {Q("Version")} {integer} NOT NULL)",
            $"CREATE TABLE {Q("Halls")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("VenueId")} {guid} NOT NULL, {Q("Name")} {text} NOT NULL, {Q("Seats")} {integer} NOT NULL, {Q("IsDeleted")} {flag} NOT NULL, {Q("DeletedAt")} {when} NULL, {Q("DeletedBy")} {text} NULL)",
            $"CREATE TABLE {Q("Dossiers")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("TenantId")} {text} NOT NULL, {Q("OwnerId")} {text} NOT NULL, {Q("Title")} {text} NOT NULL, {Q("Scopes")} {(dialect == SqlDialect.PostgreSql ? "text[]" : arrays)} NOT NULL)",
            // A table per type: the root's columns in Vehicles, each type's own in its table under the same key (VehicleView reads them together).
            $"CREATE TABLE {Q("Vehicles")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("Plate")} {text} NOT NULL, {Q("Version")} {integer} NOT NULL)",
            $"CREATE TABLE {Q("Cars")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("Seats")} {integer} NOT NULL)",
            $"CREATE TABLE {Q("SportsCars")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("TopSpeed")} {integer} NOT NULL)",
            $"CREATE TABLE {Q("Trucks")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("Payload")} {money} NOT NULL)",
            $"CREATE TABLE {Q("TagNotes")} ({Q("Id")} {guid} NOT NULL PRIMARY KEY, {Q("TagOrderId")} {guid} NOT NULL, {Q("TagTag")} {text}{dialect switch { SqlDialect.SqlServer => " COLLATE Latin1_General_100_BIN2", SqlDialect.MySql => " COLLATE utf8mb4_bin", _ => "" }} NOT NULL, {Q("Text")} {text} NOT NULL)", // TagTag as the key it references (OrderTags.Tag, a code: binary collation)
        ]);
    }

    private static IReadOnlyList<string> Drop(SqlDialect dialect)
    {
        var drops = Tables.Select(table => dialect switch
        {
            SqlDialect.SqlServer => $"IF OBJECT_ID('{table}', 'U') IS NOT NULL DROP TABLE [{table}]",
            SqlDialect.Oracle => $"DROP TABLE IF EXISTS \"{table}\" CASCADE CONSTRAINTS PURGE",
            _ => $"DROP TABLE IF EXISTS \"{table}\"",
        }).ToList();
        drops.AddRange(SqlTemporal.DropSql<Price>(dialect)); // PostgreSQL and SQLite: the history table the triggers wrote
        drops.AddRange(SqlFullText.DropSql<Ticket>(dialect)); // SQLite: the FTS5 table over Tickets
        if (dialect == SqlDialect.SqlServer)
        {
            // A system-versioned table drops only with its versioning off, then its history table.
            drops.Insert(0, "IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Prices' AND temporal_type = 2) ALTER TABLE [Prices] SET (SYSTEM_VERSIONING = OFF)");
            drops.Add("IF OBJECT_ID('PricesHistory', 'U') IS NOT NULL DROP TABLE [PricesHistory]");
        }

        return drops;
    }
}
