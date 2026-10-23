using System;
using System.Collections.Generic;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     How each function of <c>Sql</c> renders in each dialect: one place, compiled into the runtime and linked into the
///     generator (as <c>SqlDialect.cs</c> is), so the SQL both emit agrees by construction. An entry is found by the method's
///     name and arity (overloads of a name share the rendering: the argument is already a fragment). Arguments arrive
///     rendered (a column, a parameter placeholder, a literal); a <c>DatePart</c> argument arrives as its name. An entry
///     may mark a dialect's rendering as an emulation, with the note the generator reports (PRAG3012).
/// </summary>
internal static partial class SqlCatalog
{
    /// <summary>A function of the catalog: its rendering per dialect, which argument is the date part, whether the result is a predicate, and the emulations.</summary>
    internal sealed class Entry
    {
        public Entry(string name, int arity, Func<SqlDialect, string[], string> render, int partIndex = -1, bool isPredicate = false, string? sqliteEmulation = null, string? postgreSqlEmulation = null, string? sqlServerEmulation = null, bool isWindow = false, string? mySqlEmulation = null, string? oracleEmulation = null, int literalIndex = -1)
        {
            Name = name;
            Arity = arity;
            Render = render;
            PartIndex = partIndex;
            LiteralIndex = literalIndex;
            IsPredicate = isPredicate;
            SqliteEmulation = sqliteEmulation;
            PostgreSqlEmulation = postgreSqlEmulation;
            SqlServerEmulation = sqlServerEmulation;
            MySqlEmulation = mySqlEmulation;
            OracleEmulation = oracleEmulation;
            IsWindow = isWindow;
        }

        public string? OracleEmulation { get; }

        /// <summary>True for a window function (OVER): a value of the select list or an ordering, never a predicate.</summary>
        public bool IsWindow { get; }

        public string Name { get; }

        public int Arity { get; }

        public Func<SqlDialect, string[], string> Render { get; }

        /// <summary>The index of the <c>DatePart</c> argument, which must be a constant; -1 when the function takes none.</summary>
        public int PartIndex { get; }

        /// <summary>The index of a non-negative integer argument written into the statement as a literal (a bucket or row count); -1 when the function takes none.</summary>
        public int LiteralIndex { get; }

        /// <summary>True when the result is a SQL predicate (LIKE), not a value to compare with true.</summary>
        public bool IsPredicate { get; }

        public string? SqliteEmulation { get; }

        public string? PostgreSqlEmulation { get; }

        public string? SqlServerEmulation { get; }

        public string? MySqlEmulation { get; }

        /// <summary>The emulation note of a dialect, null where the rendering is native.</summary>
        public string? Emulation(SqlDialect dialect) => dialect switch
        {
            SqlDialect.Sqlite => SqliteEmulation,
            SqlDialect.PostgreSql => PostgreSqlEmulation,
            SqlDialect.MySql => MySqlEmulation,
            SqlDialect.Oracle => OracleEmulation,
            _ => SqlServerEmulation,
        };
    }

    private const string OracleFullText = "Oracle Text's CONTAINS needs a CONTEXT index on the column: the match is a case-insensitive LIKE on the whole query, the rank the occurrences of it";

    private const string SqliteDates = "SQLite has no date type: the column is ISO text, read with strftime/julianday; a value with an offset other than +00:00 is read as written";
    private const string SqliteVectors = "SQLite has no vector type or index: the distance is the application's function over the stored text (register SqlVectors.Cosine/L2/Dot as vec_cosine/vec_l2/vec_dot on the connection), a full scan";
    private const string MySqlVectors = "MySQL has no vector type or index: the distance calls vec_cosine/vec_l2/vec_dot, stored functions the application defines over the stored text; a full scan";
    // SQLite's clock as the library writes a DateTime there (yyyy-MM-dd HH:mm:ss.fffffff): comparable with the stored text, and read back by its reader.
    private const string SqliteNow = "(strftime('%Y-%m-%d %H:%M:%f', 'now') || '0000')";

    private const string SqliteFullText = "SQLite's FTS5 needs a virtual table of its own: the match is a case-insensitive LIKE on the whole query, the rank the occurrences of it";
    private const string SqlServerFullText = "SQL Server's CONTAINS/FREETEXT need the Full-Text component (not in the container image): the match is a case-insensitive LIKE on the whole query, the rank the occurrences of it";
    private const string MySqlFullText = "MySQL's MATCH … AGAINST needs a FULLTEXT index on the column: the match is a case-insensitive LIKE on the whole query, the rank the occurrences of it";

    private static readonly Entry[] Entries =
    [
        // strings
        new("Length", 1, (d, a) => d switch { SqlDialect.SqlServer => $"LEN({a[0]})", SqlDialect.MySql => $"CHAR_LENGTH({a[0]})", _ => $"LENGTH({a[0]})" }),
        new("Substring", 3, (d, a) => d switch
        {
            SqlDialect.Sqlite or SqlDialect.Oracle => $"SUBSTR({a[0]}, {a[1]} + 1, {a[2]})",
            SqlDialect.PostgreSql => $"SUBSTRING({a[0]} FROM {a[1]} + 1 FOR {a[2]})",
            _ => $"SUBSTRING({a[0]}, {a[1]} + 1, {a[2]})",
        }),
        new("Trim", 1, (_, a) => $"TRIM({a[0]})"),
        new("Replace", 3, (_, a) => $"REPLACE({a[0]}, {a[1]}, {a[2]})"),
        new("Concat", 2, (d, a) => d is SqlDialect.SqlServer or SqlDialect.MySql ? $"CONCAT({a[0]}, {a[1]})" : $"({a[0]} || {a[1]})"),
        new("Like", 2, (_, a) => $"{a[0]} LIKE {a[1]}", isPredicate: true),
        // dates
        new("Year", 1, (d, a) => Part(d, "year", "%Y", a[0]), sqliteEmulation: SqliteDates),
        new("Month", 1, (d, a) => Part(d, "month", "%m", a[0]), sqliteEmulation: SqliteDates),
        new("Day", 1, (d, a) => Part(d, "day", "%d", a[0]), sqliteEmulation: SqliteDates),
        new("Hour", 1, (d, a) => Part(d, "hour", "%H", a[0]), sqliteEmulation: SqliteDates),
        new("DateDiff", 3, (d, a) => DateDiff(d, a[0], a[1], a[2]), partIndex: 0, sqliteEmulation: SqliteDates, postgreSqlEmulation: "PostgreSQL has no DATEDIFF: whole units from the interval (EXTRACT/age), truncated towards zero"),
        new("DateAdd", 3, (d, a) => DateAdd(d, a[0], a[1], a[2]), partIndex: 0, sqliteEmulation: SqliteDates + "; the result is 'YYYY-MM-DD HH:MM:SS' text, not the stored ISO form"),
        new("Truncate", 2, (d, a) => Truncate(d, a[0], a[1]), partIndex: 0, sqliteEmulation: SqliteDates + "; the result is text in the stored ISO form", mySqlEmulation: "MySQL has no DATE_TRUNC: DATE_FORMAT to the unit, cast back to DATETIME(6)"),
        new("Now", 0, (d, _) => d switch { SqlDialect.Sqlite => SqliteNow, SqlDialect.PostgreSql => "now()", SqlDialect.MySql => "NOW(6)", SqlDialect.Oracle => "SYS_EXTRACT_UTC(SYSTIMESTAMP)", _ => "SYSDATETIMEOFFSET()" }, sqliteEmulation: "SQLite has no clock type: UTC text without offset, comparable with the stored ISO text up to the second"),
        new("UtcNow", 0, (d, _) => d switch { SqlDialect.Sqlite => SqliteNow, SqlDialect.PostgreSql => "(now() AT TIME ZONE 'UTC')", SqlDialect.MySql => "UTC_TIMESTAMP(6)", SqlDialect.Oracle => "SYS_EXTRACT_UTC(SYSTIMESTAMP)", _ => "SYSUTCDATETIME()" }, sqliteEmulation: "SQLite has no clock type: UTC text without offset, comparable with the stored ISO text up to the second"),
        // full text: PostgreSQL's tsvector/tsquery with the 'simple' dictionary; a case-insensitive LIKE on the whole query and an occurrence count as the rank where the dialect's engine is not there
        new("FullText", 2, (d, a) => d switch
        {
            SqlDialect.PostgreSql => $"(to_tsvector('simple', {a[0]}) @@ plainto_tsquery('simple', {a[1]}))",
            SqlDialect.SqlServer => $"(LOWER({a[0]}) LIKE ('%' + LOWER({a[1]}) + '%'))",
            SqlDialect.MySql => $"(LOWER({a[0]}) LIKE CONCAT('%', LOWER({a[1]}), '%'))",
            _ => $"(LOWER({a[0]}) LIKE ('%' || LOWER({a[1]}) || '%'))",
        }, isPredicate: true, sqliteEmulation: SqliteFullText, sqlServerEmulation: SqlServerFullText, mySqlEmulation: MySqlFullText, oracleEmulation: OracleFullText),
        new("FullTextRank", 2, (d, a) => d switch
        {
            SqlDialect.PostgreSql => $"ts_rank(to_tsvector('simple', {a[0]}), plainto_tsquery('simple', {a[1]}))",
            SqlDialect.Oracle => $"((LENGTH({a[0]}) - NVL(LENGTH(REPLACE(LOWER({a[0]}), LOWER({a[1]}))), 0)) / NULLIF(LENGTH({a[1]}), 0))", // '' is NULL on Oracle: a text made of the query only has no length left
            SqlDialect.SqlServer => $"((LEN({a[0]}) - LEN(REPLACE(LOWER({a[0]}), LOWER({a[1]}), ''))) / NULLIF(LEN({a[1]}), 0))",
            SqlDialect.MySql => $"((CHAR_LENGTH({a[0]}) - CHAR_LENGTH(REPLACE(LOWER({a[0]}), LOWER({a[1]}), ''))) / NULLIF(CHAR_LENGTH({a[1]}), 0))",
            _ => $"((LENGTH({a[0]}) - LENGTH(REPLACE(LOWER({a[0]}), LOWER({a[1]}), ''))) / NULLIF(LENGTH({a[1]}), 0))",
        }, sqliteEmulation: SqliteFullText, sqlServerEmulation: SqlServerFullText, mySqlEmulation: MySqlFullText, oracleEmulation: OracleFullText),
        // vectors: the second argument cast to the dialect's vector type (a JSON text parameter); the third is the column's dimensions, which SQL Server's cast wants
        new("CosineDistance", 2, (d, a) => d switch { SqlDialect.Sqlite or SqlDialect.MySql => $"vec_cosine({a[0]}, {a[1]})", SqlDialect.PostgreSql => $"(CAST({a[0]} AS vector) <=> CAST({a[1]} AS vector))", SqlDialect.Oracle => $"VECTOR_DISTANCE({a[0]}, TO_VECTOR({a[1]}), COSINE)", _ => $"VECTOR_DISTANCE('cosine', {a[0]}, CAST({a[1]} AS VECTOR({a[2]})))" }, sqliteEmulation: SqliteVectors, mySqlEmulation: MySqlVectors),
        new("L2Distance", 2, (d, a) => d switch { SqlDialect.Sqlite or SqlDialect.MySql => $"vec_l2({a[0]}, {a[1]})", SqlDialect.PostgreSql => $"(CAST({a[0]} AS vector) <-> CAST({a[1]} AS vector))", SqlDialect.Oracle => $"VECTOR_DISTANCE({a[0]}, TO_VECTOR({a[1]}), EUCLIDEAN)", _ => $"VECTOR_DISTANCE('euclidean', {a[0]}, CAST({a[1]} AS VECTOR({a[2]})))" }, sqliteEmulation: SqliteVectors, mySqlEmulation: MySqlVectors),
        new("InnerProduct", 2, (d, a) => d switch { SqlDialect.Sqlite or SqlDialect.MySql => $"vec_dot({a[0]}, {a[1]})", SqlDialect.PostgreSql => $"(-(CAST({a[0]} AS vector) <#> CAST({a[1]} AS vector)))", SqlDialect.Oracle => $"(-VECTOR_DISTANCE({a[0]}, TO_VECTOR({a[1]}), DOT))", _ => $"(-VECTOR_DISTANCE('dot', {a[0]}, CAST({a[1]} AS VECTOR({a[2]}))))" }, sqliteEmulation: SqliteVectors, mySqlEmulation: MySqlVectors),
        // numbers
        new("Round", 2, (d, a) => d == SqlDialect.PostgreSql ? $"ROUND(CAST({a[0]} AS numeric), {a[1]})" : $"ROUND({a[0]}, {a[1]})"),
        new("Abs", 1, (_, a) => $"ABS({a[0]})"),
        // MySQL's CEILING and FLOOR of a DECIMAL give a BIGINT: cast back, the reader takes a decimal
        new("Ceiling", 1, (d, a) => d switch { SqlDialect.PostgreSql or SqlDialect.Oracle => $"CEIL({a[0]})", SqlDialect.MySql => $"CAST(CEILING({a[0]}) AS DECIMAL(18, 2))", _ => $"CEILING({a[0]})" }),
        new("Floor", 1, (d, a) => d == SqlDialect.MySql ? $"CAST(FLOOR({a[0]}) AS DECIMAL(18, 2))" : $"FLOOR({a[0]})"),
        // null handling
        new("Coalesce", 2, (_, a) => $"COALESCE({a[0]}, {a[1]})"),
        new("NullIf", 2, (_, a) => $"NULLIF({a[0]}, {a[1]})"),
        // patterns
        new("Matches", 2, (d, a) => d switch { SqlDialect.Sqlite or SqlDialect.MySql => $"{a[0]} REGEXP {a[1]}", SqlDialect.PostgreSql => $"{a[0]} ~ {a[1]}", SqlDialect.Oracle => $"REGEXP_LIKE({a[0]}, {a[1]})", _ => $"REGEXP_LIKE({a[0]}, {a[1]}) = 1" }, isPredicate: true,
            sqliteEmulation: "SQLite's REGEXP calls the application's function: register it on the connection (SqliteConnection.CreateFunction(\"regexp\", (pattern, input) => …))",
            sqlServerEmulation: "REGEXP_LIKE is SQL Server 2025 / Azure SQL Database; earlier versions have no regular expressions"),
        new("Like", 3, (_, a) => $"{a[0]} LIKE {a[1]} ESCAPE {a[2]}", isPredicate: true),
        // comparisons
        new("Between", 3, (_, a) => $"{a[0]} BETWEEN {a[1]} AND {a[2]}", isPredicate: true),
        new("IsDistinctFrom", 2, (d, a) => d switch { SqlDialect.Sqlite => $"{a[0]} IS NOT {a[1]}", SqlDialect.MySql => $"NOT ({a[0]} <=> {a[1]})", SqlDialect.Oracle => $"DECODE({a[0]}, {a[1]}, 0, 1) = 1", _ => $"{a[0]} IS DISTINCT FROM {a[1]}" }, isPredicate: true, sqlServerEmulation: "IS DISTINCT FROM is SQL Server 2022+"),
        new("Greatest", 2, (d, a) => d == SqlDialect.Sqlite ? $"MAX({a[0]}, {a[1]})" : $"GREATEST({a[0]}, {a[1]})", sqliteEmulation: "SQLite's scalar MAX: a NULL argument makes the result NULL, where GREATEST ignores it", sqlServerEmulation: "GREATEST is SQL Server 2022+", mySqlEmulation: "MySQL's GREATEST: a NULL argument makes the result NULL, where PostgreSQL's ignores it", oracleEmulation: "Oracle's GREATEST: a NULL argument makes the result NULL, where PostgreSQL's ignores it"),
        new("Least", 2, (d, a) => d == SqlDialect.Sqlite ? $"MIN({a[0]}, {a[1]})" : $"LEAST({a[0]}, {a[1]})", sqliteEmulation: "SQLite's scalar MIN: a NULL argument makes the result NULL, where LEAST ignores it", sqlServerEmulation: "LEAST is SQL Server 2022+", mySqlEmulation: "MySQL's LEAST: a NULL argument makes the result NULL, where PostgreSQL's ignores it", oracleEmulation: "Oracle's LEAST: a NULL argument makes the result NULL, where PostgreSQL's ignores it"),
        // casts
        new("ToText", 1, (d, a) => $"CAST({a[0]} AS {d switch { SqlDialect.Sqlite => "TEXT", SqlDialect.PostgreSql => "text", SqlDialect.MySql => "CHAR", SqlDialect.Oracle => "VARCHAR2(4000)", _ => "nvarchar(max)" }})"),
        new("ToInt32", 1, (d, a) => $"CAST({a[0]} AS {d switch { SqlDialect.Sqlite => "INTEGER", SqlDialect.PostgreSql => "integer", SqlDialect.MySql => "SIGNED", SqlDialect.Oracle => "NUMBER(10)", _ => "int" }})"),
        new("ToDecimal", 1, (d, a) => $"CAST({a[0]} AS {d switch { SqlDialect.Sqlite => "REAL", SqlDialect.PostgreSql => "numeric", SqlDialect.MySql => "DECIMAL(18, 2)", SqlDialect.Oracle => "NUMBER(18, 2)", _ => "decimal(18, 2)" }})", sqliteEmulation: "SQLite has no decimal: REAL"),
        new("ToDouble", 1, (d, a) => $"CAST({a[0]} AS {d switch { SqlDialect.Sqlite => "REAL", SqlDialect.PostgreSql => "double precision", SqlDialect.MySql => "DOUBLE", SqlDialect.Oracle => "BINARY_DOUBLE", _ => "float" }})"),
        new("DateText", 1, (d, a) => d switch { SqlDialect.Sqlite => $"strftime('%Y-%m-%d', {a[0]})", SqlDialect.PostgreSql or SqlDialect.Oracle => $"to_char({a[0]}, 'YYYY-MM-DD')", SqlDialect.MySql => $"DATE_FORMAT({a[0]}, '%Y-%m-%d')", _ => $"CONVERT(varchar(10), {a[0]}, 23)" }, sqliteEmulation: SqliteDates),
        // orderings and lists inside a window: DESC, and two or three expressions as one list
        new("Desc", 1, (_, a) => $"{a[0]} DESC"),
        new("By", 2, (_, a) => $"{a[0]}, {a[1]}"),
        new("By", 3, (_, a) => $"{a[0]}, {a[1]}, {a[2]}"),
        // window functions: ROW_NUMBER/RANK/DENSE_RANK (bigint on PostgreSQL: cast to int), LAG/LEAD, COUNT(*) OVER (), SUM OVER a partition (running with an order)
        new("RowNumber", 1, (d, a) => Int(d, $"ROW_NUMBER() OVER (ORDER BY {a[0]})"), isWindow: true),
        new("RowNumber", 2, (d, a) => Int(d, $"ROW_NUMBER() OVER (PARTITION BY {a[0]} ORDER BY {a[1]})"), isWindow: true),
        new("Rank", 1, (d, a) => Int(d, $"RANK() OVER (ORDER BY {a[0]})"), isWindow: true),
        new("Rank", 2, (d, a) => Int(d, $"RANK() OVER (PARTITION BY {a[0]} ORDER BY {a[1]})"), isWindow: true),
        new("DenseRank", 1, (d, a) => Int(d, $"DENSE_RANK() OVER (ORDER BY {a[0]})"), isWindow: true),
        new("DenseRank", 2, (d, a) => Int(d, $"DENSE_RANK() OVER (PARTITION BY {a[0]} ORDER BY {a[1]})"), isWindow: true),
        new("Lag", 3, (_, a) => $"LAG({a[0]}, {a[1]}) OVER (ORDER BY {a[2]})", isWindow: true),
        new("Lag", 4, (_, a) => $"LAG({a[0]}, {a[1]}) OVER (PARTITION BY {a[2]} ORDER BY {a[3]})", isWindow: true),
        new("Lead", 3, (_, a) => $"LEAD({a[0]}, {a[1]}) OVER (ORDER BY {a[2]})", isWindow: true),
        new("Lead", 4, (_, a) => $"LEAD({a[0]}, {a[1]}) OVER (PARTITION BY {a[2]} ORDER BY {a[3]})", isWindow: true),
        new("CountOver", 0, (d, _) => Int(d, "COUNT(*) OVER ()"), isWindow: true),
        new("SumOver", 2, (_, a) => $"SUM({a[0]}) OVER (PARTITION BY {a[1]})", isWindow: true),
        new("SumOver", 3, (_, a) => $"SUM({a[0]}) OVER (PARTITION BY {a[1]} ORDER BY {a[2]})", isWindow: true),
        // NTILE (bigint on SQL Server and MySQL: cast), FIRST_VALUE, LAST_VALUE over the whole partition (the default frame ends at the current
        // row), a moving SUM over the row and the ones before it; the bucket and row counts are literals (SQL Server takes no parameter in a frame)
        new("Ntile", 2, (d, a) => Int(d, $"NTILE({a[0]}) OVER (ORDER BY {a[1]})"), isWindow: true, literalIndex: 0),
        new("Ntile", 3, (d, a) => Int(d, $"NTILE({a[0]}) OVER (PARTITION BY {a[1]} ORDER BY {a[2]})"), isWindow: true, literalIndex: 0),
        new("FirstValue", 2, (_, a) => $"FIRST_VALUE({a[0]}) OVER (ORDER BY {a[1]})", isWindow: true),
        new("FirstValue", 3, (_, a) => $"FIRST_VALUE({a[0]}) OVER (PARTITION BY {a[1]} ORDER BY {a[2]})", isWindow: true),
        new("LastValue", 2, (_, a) => $"LAST_VALUE({a[0]}) OVER (ORDER BY {a[1]} {WholeFrame})", isWindow: true),
        new("LastValue", 3, (_, a) => $"LAST_VALUE({a[0]}) OVER (PARTITION BY {a[1]} ORDER BY {a[2]} {WholeFrame})", isWindow: true),
        new("SumOver", 4, (_, a) => $"SUM({a[0]}) OVER (PARTITION BY {a[1]} ORDER BY {a[2]} ROWS BETWEEN {a[3]} PRECEDING AND CURRENT ROW)", isWindow: true, literalIndex: 3),
        // AVG over doubles (an integer column's average is not truncated), MIN and MAX: the partition, the running frame, a moving ROWS frame.
        new("AvgOver", 2, (d, a) => $"AVG({Double(d, a[0])}) OVER (PARTITION BY {a[1]})", isWindow: true),
        new("AvgOver", 3, (d, a) => $"AVG({Double(d, a[0])}) OVER (PARTITION BY {a[1]} ORDER BY {a[2]})", isWindow: true),
        new("AvgOver", 4, (d, a) => $"AVG({Double(d, a[0])}) OVER (PARTITION BY {a[1]} ORDER BY {a[2]} ROWS BETWEEN {a[3]} PRECEDING AND CURRENT ROW)", isWindow: true, literalIndex: 3),
        new("MinOver", 2, (_, a) => $"MIN({a[0]}) OVER (PARTITION BY {a[1]})", isWindow: true),
        new("MinOver", 3, (_, a) => $"MIN({a[0]}) OVER (PARTITION BY {a[1]} ORDER BY {a[2]})", isWindow: true),
        new("MinOver", 4, (_, a) => $"MIN({a[0]}) OVER (PARTITION BY {a[1]} ORDER BY {a[2]} ROWS BETWEEN {a[3]} PRECEDING AND CURRENT ROW)", isWindow: true, literalIndex: 3),
        new("MaxOver", 2, (_, a) => $"MAX({a[0]}) OVER (PARTITION BY {a[1]})", isWindow: true),
        new("MaxOver", 3, (_, a) => $"MAX({a[0]}) OVER (PARTITION BY {a[1]} ORDER BY {a[2]})", isWindow: true),
        new("MaxOver", 4, (_, a) => $"MAX({a[0]}) OVER (PARTITION BY {a[1]} ORDER BY {a[2]} ROWS BETWEEN {a[3]} PRECEDING AND CURRENT ROW)", isWindow: true, literalIndex: 3),
        new("CountOver", 1, (d, a) => Int(d, $"COUNT(*) OVER (PARTITION BY {a[0]})"), isWindow: true),
        // A RANGE frame with an offset on the order's value (a numeric order): native, or on SQL Server a token its statement turns into a correlated subquery.
        new("SumOverRange", 4, (d, a) => Range(d, "SUM", a[0], a[1], a[2], a[3]), isWindow: true, literalIndex: 3, sqlServerEmulation: RangeEmulation),
        new("AvgOverRange", 4, (d, a) => Range(d, "AVG", Double(d, a[0]), a[1], a[2], a[3]), isWindow: true, literalIndex: 3, sqlServerEmulation: RangeEmulation),
        new("MinOverRange", 4, (d, a) => Range(d, "MIN", a[0], a[1], a[2], a[3]), isWindow: true, literalIndex: 3, sqlServerEmulation: RangeEmulation),
        new("MaxOverRange", 4, (d, a) => Range(d, "MAX", a[0], a[1], a[2], a[3]), isWindow: true, literalIndex: 3, sqlServerEmulation: RangeEmulation),
        new("CountOverRange", 3, (d, a) => Int(d, Range(d, "COUNT", "*", a[0], a[1], a[2])), isWindow: true, literalIndex: 2, sqlServerEmulation: RangeEmulation),
        // A named window (Sql.Window): its specification as a token the statement names in its WINDOW clause; the functions that take it read OVER it.
        new("Window", 1, (_, a) => "{w:ORDER BY " + a[0] + ":w}", isWindow: true),
        new("Window", 2, (_, a) => "{w:PARTITION BY " + a[0] + " ORDER BY " + a[1] + ":w}", isWindow: true),
        new("SumOverWindow", 2, (_, a) => $"SUM({a[0]}) OVER {a[1]}", isWindow: true),
        new("AvgOverWindow", 2, (d, a) => $"AVG({Double(d, a[0])}) OVER {a[1]}", isWindow: true),
        new("MinOverWindow", 2, (_, a) => $"MIN({a[0]}) OVER {a[1]}", isWindow: true),
        new("MaxOverWindow", 2, (_, a) => $"MAX({a[0]}) OVER {a[1]}", isWindow: true),
        new("CountOverWindow", 1, (d, a) => Int(d, $"COUNT(*) OVER {a[0]}"), isWindow: true),
        new("RowNumberWindow", 1, (d, a) => Int(d, $"ROW_NUMBER() OVER {a[0]}"), isWindow: true),
        // ranges (SqlDateRange.Contains: the two bound columns and the day)
        new("RangeContains", 3, (d, a) => d == SqlDialect.PostgreSql ? $"daterange({a[0]}, {a[1]}, '[]') @> {a[2]}" : $"(({a[0]} IS NULL OR {a[0]} <= {a[2]}) AND ({a[1]} IS NULL OR {a[2]} <= {a[1]}))", isPredicate: true,
            sqliteEmulation: "SQLite has no range type: the two bounds are compared (an open bound is NULL)", sqlServerEmulation: "SQL Server has no range type: the two bounds are compared (an open bound is NULL)", mySqlEmulation: "MySQL has no range type: the two bounds are compared (an open bound is NULL)", oracleEmulation: "Oracle has no range type: the two bounds are compared (an open bound is NULL)"),
    ];

    /// <summary>The entry of a <see cref="Sql" /> method by name and argument count; null when the catalog has none.</summary>
    public static Entry? Find(string name, int arity)
    {
        foreach (var entry in Entries)
        {
            if (entry.Name == name && entry.Arity == arity)
                return entry;
        }

        foreach (var entry in JsonEntries)
        {
            if (entry.Name == name && entry.Arity == arity)
                return entry;
        }

        foreach (var entry in FullTextEntries)
        {
            if (entry.Name == name && entry.Arity == arity)
                return entry;
        }

        return null;
    }

    /// <summary>
    ///     A member of a JSON column as a value of the member's kind: PostgreSQL <c>->></c> (one level) or <c>#>></c> (a path) give text, cast to the
    ///     kind; SQLite's <c>json_extract</c> keeps the JSON type; SQL Server's <c>JSON_VALUE</c> gives nvarchar, cast to the kind; MySQL's
    ///     <c>JSON_EXTRACT</c> keeps the JSON type, unquoted for text, cast for the numbers. Kinds: text, int, long, decimal, double, bool, datetime, uuid.
    /// </summary>
    private static string JsonValue(SqlDialect dialect, string[] a)
    {
        var (column, path, kind) = (a[0], a[1], a[2]);
        switch (dialect)
        {
            case SqlDialect.Sqlite:
                return $"json_extract({column}, '$.{path}')";
            case SqlDialect.Oracle:
                return kind switch
                {
                    "int" or "long" or "decimal" => $"JSON_VALUE({column}, '$.{path}' RETURNING NUMBER)",
                    "double" => $"JSON_VALUE({column}, '$.{path}' RETURNING BINARY_DOUBLE)",
                    "bool" => $"(JSON_VALUE({column}, '$.{path}') = 'true')",
                    "datetime" => $"JSON_VALUE({column}, '$.{path}' RETURNING TIMESTAMP)",
                    _ => $"JSON_VALUE({column}, '$.{path}')",
                };
            case SqlDialect.MySql:
            {
                var json = $"JSON_EXTRACT({column}, '$.{path}')";
                return kind switch
                {
                    "int" or "long" => $"CAST({json} AS SIGNED)",
                    "decimal" => $"CAST({json} AS DECIMAL(18, 2))",
                    "double" => $"CAST({json} AS DOUBLE)",
                    "bool" => $"({json} = true)",
                    "datetime" => $"CAST(JSON_UNQUOTE({json}) AS DATETIME(6))",
                    _ => $"JSON_UNQUOTE({json})",
                };
            }
            case SqlDialect.PostgreSql:
            {
                var text = path.IndexOf('.') >= 0 ? $"{column} #>> '{{{path.Replace('.', ',')}}}'" : $"{column} ->> '{path}'"; // IndexOf: the file compiles for netstandard2.0 too
                var type = kind switch { "int" => "integer", "long" => "bigint", "decimal" => "numeric", "double" => "double precision", "bool" => "boolean", "datetime" => "timestamptz", "uuid" => "uuid", _ => null };
                return type is null ? $"({text})" : $"CAST({text} AS {type})";
            }

            default:
            {
                var text = $"JSON_VALUE({column}, '$.{path}')";
                var type = kind switch { "int" => "int", "long" => "bigint", "decimal" => "decimal(18, 2)", "double" => "float", "bool" => "bit", "datetime" => "datetimeoffset", "uuid" => "uniqueidentifier", _ => null };
                return type is null ? text : $"CAST({text} AS {type})";
            }
        }
    }

    /// <summary>A number the dialect gives as a 64-bit integer (ROW_NUMBER, a window count), read as a 32-bit one: cast on PostgreSQL and SQL Server, whose readers take a bigint as it is; SQLite reads it as asked.</summary>
    private const string WholeFrame = "ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING";

    private const string RangeEmulation = "SQL Server frames RANGE by UNBOUNDED or CURRENT ROW only: the frame with an offset is a correlated subquery over the entity's table under the statement's predicates";

    private static string Range(SqlDialect dialect, string function, string value, string partition, string order, string offset) => dialect == SqlDialect.SqlServer
        ? "{rng:" + function + "|~|" + value + "|~|" + partition + "|~|" + order + "|~|" + offset + ":rng}"
        : $"{function}({value}) OVER (PARTITION BY {partition} ORDER BY {order} RANGE BETWEEN {offset} PRECEDING AND CURRENT ROW)";

    private static string Double(SqlDialect dialect, string value) => $"CAST({value} AS {dialect switch { SqlDialect.Sqlite => "REAL", SqlDialect.PostgreSql => "double precision", SqlDialect.MySql => "DOUBLE", SqlDialect.Oracle => "BINARY_DOUBLE", _ => "float" }})";

    private static string Int(SqlDialect dialect, string value) => dialect switch { SqlDialect.PostgreSql => $"CAST({value} AS integer)", SqlDialect.SqlServer => $"CAST({value} AS int)", SqlDialect.MySql => $"CAST({value} AS SIGNED)", SqlDialect.Oracle => $"CAST({value} AS NUMBER(10))", _ => value };

    private static string Part(SqlDialect dialect, string sqlServerPart, string sqliteFormat, string value) => dialect switch
    {
        SqlDialect.Sqlite => $"CAST(strftime('{sqliteFormat}', {value}) AS INTEGER)",
        SqlDialect.PostgreSql => $"CAST(EXTRACT({sqlServerPart.ToUpperInvariant()} FROM {value}) AS integer)",
        SqlDialect.MySql or SqlDialect.Oracle => $"EXTRACT({sqlServerPart.ToUpperInvariant()} FROM {value})",
        _ => $"DATEPART({sqlServerPart}, {value})",
    };

    private static string DateDiff(SqlDialect dialect, string part, string from, string to)
    {
        var unit = part.ToLowerInvariant();
        switch (dialect)
        {
            case SqlDialect.SqlServer:
                return $"DATEDIFF({unit}, {from}, {to})";
            case SqlDialect.MySql:
                return $"TIMESTAMPDIFF({unit.ToUpperInvariant()}, {from}, {to})";
            case SqlDialect.Oracle:
                // Whole units, truncated towards zero: months from MONTHS_BETWEEN, the others from the difference of the instants as DATEs (in days).
                return unit switch
                {
                    "year" => $"CAST(TRUNC(MONTHS_BETWEEN({to}, {from}) / 12) AS NUMBER(10))",
                    "month" => $"CAST(TRUNC(MONTHS_BETWEEN({to}, {from})) AS NUMBER(10))",
                    _ => $"CAST(TRUNC((CAST({to} AS DATE) - CAST({from} AS DATE)) * {(86400 / Seconds(unit)).ToString(System.Globalization.CultureInfo.InvariantCulture)}) AS NUMBER(10))",
                };
            case SqlDialect.PostgreSql:
                return unit switch
                {
                    "year" => $"CAST(EXTRACT(YEAR FROM age({to}, {from})) AS integer)",
                    "month" => $"CAST(EXTRACT(YEAR FROM age({to}, {from})) * 12 + EXTRACT(MONTH FROM age({to}, {from})) AS integer)",
                    _ => $"CAST(TRUNC(EXTRACT(EPOCH FROM ({to} - {from})) / {Seconds(unit)}) AS integer)",
                };
            default:
                return unit switch
                {
                    "year" => $"(CAST(strftime('%Y', {to}) AS INTEGER) - CAST(strftime('%Y', {from}) AS INTEGER))",
                    "month" => $"((CAST(strftime('%Y', {to}) AS INTEGER) - CAST(strftime('%Y', {from}) AS INTEGER)) * 12 + CAST(strftime('%m', {to}) AS INTEGER) - CAST(strftime('%m', {from}) AS INTEGER))",
                    _ => $"CAST((julianday({to}) - julianday({from})) * {86400 / Seconds(unit)} AS INTEGER)",
                };
        }
    }

    private static int Seconds(string unit) => unit switch { "day" => 86400, "hour" => 3600, "minute" => 60, _ => 1 };

    private static string DateAdd(SqlDialect dialect, string part, string count, string value)
    {
        var unit = part.ToLowerInvariant();
        return dialect switch
        {
            SqlDialect.SqlServer => $"DATEADD({unit}, {count}, {value})",
            SqlDialect.PostgreSql => $"({value} + make_interval({unit switch { "year" => "years", "month" => "months", "day" => "days", "hour" => "hours", "minute" => "mins", _ => "secs" }} => {count}))",
            SqlDialect.MySql => $"DATE_ADD({value}, INTERVAL {count} {unit.ToUpperInvariant()})",
            SqlDialect.Oracle => unit is "year" or "month" ? $"({value} + NUMTOYMINTERVAL({count}, '{unit.ToUpperInvariant()}'))" : $"({value} + NUMTODSINTERVAL({count}, '{unit.ToUpperInvariant()}'))",
            _ => $"datetime({value}, ({count}) || ' {unit}s')",
        };
    }

    private static string Truncate(SqlDialect dialect, string part, string value)
    {
        var unit = part.ToLowerInvariant();
        return dialect switch
        {
            SqlDialect.SqlServer => $"DATETRUNC({unit}, {value})",
            SqlDialect.PostgreSql => $"date_trunc('{unit}', {value})",
            SqlDialect.Oracle => unit == "second" ? $"CAST(CAST({value} AS DATE) AS TIMESTAMP)" : $"CAST(TRUNC({value}, '{unit switch { "year" => "YYYY", "month" => "MM", "day" => "DD", "hour" => "HH24", _ => "MI" }}') AS TIMESTAMP)",
            SqlDialect.MySql => $"CAST(DATE_FORMAT({value}, '{unit switch { "year" => "%Y-01-01 00:00:00", "month" => "%Y-%m-01 00:00:00", "day" => "%Y-%m-%d 00:00:00", "hour" => "%Y-%m-%d %H:00:00", "minute" => "%Y-%m-%d %H:%i:00", _ => "%Y-%m-%d %H:%i:%s" }}') AS DATETIME(6))",
            _ => unit switch
            {
                "year" => $"strftime('%Y-01-01T00:00:00', {value})",
                "month" => $"strftime('%Y-%m-01T00:00:00', {value})",
                "day" => $"strftime('%Y-%m-%dT00:00:00', {value})",
                "hour" => $"strftime('%Y-%m-%dT%H:00:00', {value})",
                "minute" => $"strftime('%Y-%m-%dT%H:%M:00', {value})",
                _ => $"strftime('%Y-%m-%dT%H:%M:%S', {value})",
            },
        };
    }
}
