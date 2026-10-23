namespace Pragmatic.Persistence.Sql;

/// <summary>The unit of a date function (<c>Sql.DateDiff</c>, <c>Sql.DateAdd</c>, <c>Sql.Truncate</c>): a constant at the call site, the generator reads it.</summary>
public enum DatePart
{
    Year,
    Month,
    Day,
    Hour,
    Minute,
    Second,
}

/// <summary>
///     The typed catalog of SQL functions: methods with real types, recognised by symbol in a chain's lambdas and rendered
///     for each dialect by the translator (the rendering lives in <c>SqlCatalog</c>, one place for the generator and the
///     runtime). Never executed in .NET: a call outside a translated lambda throws. Where a dialect renders an emulation
///     (dates on SQLite are ISO text), the generator says so at the call site (PRAG3012). An application declares its own
///     functions with <c>[SqlFunction]</c> and a typed fragment per dialect.
/// </summary>
public static class Sql
{
    // Strings

    /// <summary>The length in characters: LENGTH (SQLite, PostgreSQL), LEN (SQL Server).</summary>
    public static int Length(string value) => throw NotTranslated();

    /// <summary>A substring with .NET's zero-based start: SUBSTR / SUBSTRING with start + 1.</summary>
    public static string Substring(string value, int start, int length) => throw NotTranslated();

    public static string Trim(string value) => throw NotTranslated();

    public static string Replace(string value, string oldValue, string newValue) => throw NotTranslated();

    /// <summary>Concatenation: <c>||</c> (SQLite, PostgreSQL), CONCAT (SQL Server; a NULL part reads as empty there, as NULL elsewhere).</summary>
    public static string Concat(string left, string right) => throw NotTranslated();

    /// <summary>LIKE with the pattern as given: the caller writes the wildcards (<c>%</c>, <c>_</c>); <c>Contains</c> on a string escapes them instead.</summary>
    public static bool Like(string value, string pattern) => throw NotTranslated();

    // Dates: DateTimeOffset and DateTime

    public static int Year(DateTimeOffset value) => throw NotTranslated();

    public static int Year(DateTime value) => throw NotTranslated();

    public static int Month(DateTimeOffset value) => throw NotTranslated();

    public static int Month(DateTime value) => throw NotTranslated();

    public static int Day(DateTimeOffset value) => throw NotTranslated();

    public static int Day(DateTime value) => throw NotTranslated();

    public static int Hour(DateTimeOffset value) => throw NotTranslated();

    public static int Hour(DateTime value) => throw NotTranslated();

    /// <summary>Whole units from <paramref name="from" /> to <paramref name="to" /> (DATEDIFF on SQL Server; EXTRACT/age on PostgreSQL; julianday/strftime on SQLite).</summary>
    public static int DateDiff(DatePart part, DateTimeOffset from, DateTimeOffset to) => throw NotTranslated();

    public static int DateDiff(DatePart part, DateTime from, DateTime to) => throw NotTranslated();

    /// <summary>The date moved by <paramref name="count" /> units (DATEADD; make_interval on PostgreSQL; datetime modifiers on SQLite).</summary>
    public static DateTimeOffset DateAdd(DatePart part, int count, DateTimeOffset value) => throw NotTranslated();

    public static DateTime DateAdd(DatePart part, int count, DateTime value) => throw NotTranslated();

    /// <summary>The date cut at the unit (DATETRUNC; date_trunc; strftime on SQLite).</summary>
    public static DateTimeOffset Truncate(DatePart part, DateTimeOffset value) => throw NotTranslated();

    public static DateTime Truncate(DatePart part, DateTime value) => throw NotTranslated();

    /// <summary>The server's current time with its offset (SYSDATETIMEOFFSET, now(); UTC text on SQLite).</summary>
    public static DateTimeOffset Now() => throw NotTranslated();

    /// <summary>The server's current UTC time (SYSUTCDATETIME, now() AT TIME ZONE 'UTC'; UTC text on SQLite).</summary>
    public static DateTime UtcNow() => throw NotTranslated();

    // Full text.

    /// <summary>
    ///     True when the text column matches the words of <paramref name="query" />: PostgreSQL <c>to_tsvector('simple', col) @@ plainto_tsquery('simple', q)</c>
    ///     (every word, stemmed by the 'simple' dictionary); on SQLite and SQL Server an emulation (the query as a whole, case-insensitive LIKE:
    ///     FTS5 needs a virtual table, CONTAINS the Full-Text component), reported (PRAG3012).
    /// </summary>
    public static bool FullText(string column, string query) => throw NotTranslated();

    /// <summary>The match's rank, larger is better: <c>ts_rank</c> on PostgreSQL; the occurrences of the query in the text where the match is emulated.</summary>
    public static double FullTextRank(string column, string query) => throw NotTranslated();

    // Vectors ([Vector(n)] float[] columns): a k-NN is OrderBy(x => Sql.CosineDistance(x.Vector, query)).Take(k).

    /// <summary>The cosine distance between a vector column and a vector (pgvector <c>&lt;=&gt;</c>, SQL Server 2025 <c>VECTOR_DISTANCE('cosine')</c>; the application's <c>vec_cosine</c> on SQLite).</summary>
    public static double CosineDistance(float[] column, float[] vector) => throw NotTranslated();

    /// <summary>The Euclidean distance (<c>&lt;-&gt;</c>, <c>VECTOR_DISTANCE('euclidean')</c>; <c>vec_l2</c> on SQLite).</summary>
    public static double L2Distance(float[] column, float[] vector) => throw NotTranslated();

    /// <summary>The inner product (pgvector's <c>&lt;#&gt;</c> negated, <c>VECTOR_DISTANCE('dot')</c> negated; <c>vec_dot</c> on SQLite): larger is closer, so a k-NN orders by it descending.</summary>
    public static double InnerProduct(float[] column, float[] vector) => throw NotTranslated();

    // Numbers

    public static decimal Round(decimal value, int digits) => throw NotTranslated();

    public static double Round(double value, int digits) => throw NotTranslated();

    public static decimal Abs(decimal value) => throw NotTranslated();

    public static int Abs(int value) => throw NotTranslated();

    public static decimal Ceiling(decimal value) => throw NotTranslated();

    public static decimal Floor(decimal value) => throw NotTranslated();

    // Null handling

    /// <summary>COALESCE: the first value that is not NULL. The <c>??</c> operator on a column translates to the same.</summary>
    public static T Coalesce<T>(T? value, T fallback) => throw NotTranslated();

    /// <summary>NULLIF: NULL when the two are equal, the value otherwise.</summary>
    public static T? NullIf<T>(T value, T other) where T : struct => throw NotTranslated();

    public static string? NullIf(string value, string other) => throw NotTranslated();

    // Patterns

    /// <summary>
    ///     A regular expression: <c>~</c> on PostgreSQL; <c>REGEXP</c> on SQLite, which needs the application's function on the connection
    ///     (<c>SqliteConnection.CreateFunction("regexp", …)</c>); <c>REGEXP_LIKE</c> on SQL Server 2025 / Azure SQL (both reported, PRAG3012).
    /// </summary>
    public static bool Matches(string value, string pattern) => throw NotTranslated();

    /// <summary>LIKE with the caller's escape character: <c>value LIKE pattern ESCAPE escape</c>.</summary>
    public static bool Like(string value, string pattern, string escape) => throw NotTranslated();

    // Comparisons

    /// <summary>BETWEEN, both ends included.</summary>
    public static bool Between<T>(T value, T low, T high) where T : IComparable<T> => throw NotTranslated();

    /// <summary>A null-safe inequality: <c>IS DISTINCT FROM</c> (SQL Server 2022+), <c>IS NOT</c> on SQLite; two NULLs are not distinct.</summary>
    public static bool IsDistinctFrom<T>(T left, T right) => throw NotTranslated();

    /// <summary>GREATEST of two values (SQL Server 2022+); MAX as a scalar on SQLite, where a NULL argument makes the result NULL.</summary>
    public static T Greatest<T>(T left, T right) where T : IComparable<T> => throw NotTranslated();

    /// <summary>LEAST of two values (SQL Server 2022+); MIN as a scalar on SQLite, where a NULL argument makes the result NULL.</summary>
    public static T Least<T>(T left, T right) where T : IComparable<T> => throw NotTranslated();

    /// <summary>
    ///     A group's values as one JSON array, in a grouped Select: <c>Sql.JsonArray(g.Select(x => x.Sku))</c> → <c>["a","b"]</c> (json_agg,
    ///     JSON_ARRAYAGG, json_group_array; STRING_AGG of the encoded elements on SQL Server). Strings, Guids, integers, decimals, booleans.
    /// </summary>
    public static string JsonArray<T>(IEnumerable<T> values) => throw NotTranslated();

    // Windows: values of the select list or an ordering, never a predicate (DistinctOn filters on a row number).

    /// <summary>An ordering direction inside a window or a DistinctOn: <c>value DESC</c>.</summary>
    public static T Desc<T>(T value) => throw NotTranslated();

    /// <summary>Two or three expressions as one list: a composite partition or ordering.</summary>
    public static (TA, TB) By<TA, TB>(TA first, TB second) => throw NotTranslated();

    public static (TA, TB, TC) By<TA, TB, TC>(TA first, TB second, TC third) => throw NotTranslated();

    /// <summary>ROW_NUMBER() OVER (ORDER BY order): 1 for the first row of the ordering.</summary>
    public static int RowNumber<TOrder>(TOrder order) => throw NotTranslated();

    /// <summary>ROW_NUMBER() OVER (PARTITION BY partition ORDER BY order): 1 for the first row of each partition.</summary>
    public static int RowNumber<TPartition, TOrder>(TPartition partition, TOrder order) => throw NotTranslated();

    public static int Rank<TOrder>(TOrder order) => throw NotTranslated();

    public static int Rank<TPartition, TOrder>(TPartition partition, TOrder order) => throw NotTranslated();

    public static int DenseRank<TOrder>(TOrder order) => throw NotTranslated();

    public static int DenseRank<TPartition, TOrder>(TPartition partition, TOrder order) => throw NotTranslated();

    /// <summary>LAG(value, offset) OVER (ORDER BY order): the value <paramref name="offset" /> rows before, null at the edge (declare the member nullable: a cast).</summary>
    public static T? Lag<T, TOrder>(T value, int offset, TOrder order) => throw NotTranslated();

    public static T? Lag<T, TPartition, TOrder>(T value, int offset, TPartition partition, TOrder order) => throw NotTranslated();

    public static T? Lead<T, TOrder>(T value, int offset, TOrder order) => throw NotTranslated();

    public static T? Lead<T, TPartition, TOrder>(T value, int offset, TPartition partition, TOrder order) => throw NotTranslated();

    /// <summary>COUNT(*) OVER (): the rows the statement selects before paging, on every row (a page and its total in one round trip).</summary>
    public static int CountOver() => throw NotTranslated();

    /// <summary>SUM(value) OVER (PARTITION BY partition): the partition's total on every row.</summary>
    public static T SumOver<T, TPartition>(T value, TPartition partition) => throw NotTranslated();

    /// <summary>SUM(value) OVER (PARTITION BY partition ORDER BY order): the running total within the partition.</summary>
    public static T SumOver<T, TPartition, TOrder>(T value, TPartition partition, TOrder order) => throw NotTranslated();

    /// <summary>SUM(value) OVER (PARTITION BY partition ORDER BY order ROWS BETWEEN preceding PRECEDING AND CURRENT ROW): a moving sum; <paramref name="preceding" /> a constant.</summary>
    public static T SumOver<T, TPartition, TOrder>(T value, TPartition partition, TOrder order, int preceding) => throw NotTranslated();

    /// <summary>AVG(value) OVER (PARTITION BY partition), as a double (an integer column's average is not truncated): the partition's mean on every row.</summary>
    public static double AvgOver<T, TPartition>(T value, TPartition partition) => throw NotTranslated();

    /// <summary>The running mean within the partition (ORDER BY order).</summary>
    public static double AvgOver<T, TPartition, TOrder>(T value, TPartition partition, TOrder order) => throw NotTranslated();

    /// <summary>A moving mean over the row and the <paramref name="preceding" /> rows before it (ROWS frame); <paramref name="preceding" /> a constant.</summary>
    public static double AvgOver<T, TPartition, TOrder>(T value, TPartition partition, TOrder order, int preceding) => throw NotTranslated();

    /// <summary>MIN(value) OVER (PARTITION BY partition): the partition's smallest value on every row.</summary>
    public static T MinOver<T, TPartition>(T value, TPartition partition) => throw NotTranslated();

    public static T MinOver<T, TPartition, TOrder>(T value, TPartition partition, TOrder order) => throw NotTranslated();

    public static T MinOver<T, TPartition, TOrder>(T value, TPartition partition, TOrder order, int preceding) => throw NotTranslated();

    /// <summary>MAX(value) OVER (PARTITION BY partition): the partition's largest value on every row.</summary>
    public static T MaxOver<T, TPartition>(T value, TPartition partition) => throw NotTranslated();

    public static T MaxOver<T, TPartition, TOrder>(T value, TPartition partition, TOrder order) => throw NotTranslated();

    public static T MaxOver<T, TPartition, TOrder>(T value, TPartition partition, TOrder order, int preceding) => throw NotTranslated();

    /// <summary>COUNT(*) OVER (PARTITION BY partition): the partition's rows on every row.</summary>
    public static int CountOver<TPartition>(TPartition partition) => throw NotTranslated();

    /// <summary>
    ///     SUM(value) over the rows of the partition whose order value is within <paramref name="range" /> below the row's (RANGE BETWEEN range
    ///     PRECEDING AND CURRENT ROW: ties included, gaps honoured, unlike a ROWS frame); a numeric order, <paramref name="range" /> a constant. On
    ///     SQL Server, which has no such frame, a correlated subquery over the statement's rows.
    /// </summary>
    public static T SumOverRange<T, TPartition, TOrder>(T value, TPartition partition, TOrder order, int range) => throw NotTranslated();

    public static double AvgOverRange<T, TPartition, TOrder>(T value, TPartition partition, TOrder order, int range) => throw NotTranslated();

    public static T MinOverRange<T, TPartition, TOrder>(T value, TPartition partition, TOrder order, int range) => throw NotTranslated();

    public static T MaxOverRange<T, TPartition, TOrder>(T value, TPartition partition, TOrder order, int range) => throw NotTranslated();

    public static int CountOverRange<TPartition, TOrder>(TPartition partition, TOrder order, int range) => throw NotTranslated();

    /// <summary>
    ///     A window (PARTITION BY partition ORDER BY order) the *Over functions share: the statement names it once in its WINDOW clause
    ///     (<c>WINDOW w1 AS (…)</c>, every dialect here has it) and each function reads <c>OVER w1</c>.
    /// </summary>
    public static SqlWindow Window<TPartition, TOrder>(TPartition partition, TOrder order) => throw NotTranslated();

    /// <summary>A window over the whole result in an order.</summary>
    public static SqlWindow Window<TOrder>(TOrder order) => throw NotTranslated();

    public static T SumOver<T>(T value, SqlWindow window) => throw NotTranslated();

    public static double AvgOver<T>(T value, SqlWindow window) => throw NotTranslated();

    public static T MinOver<T>(T value, SqlWindow window) => throw NotTranslated();

    public static T MaxOver<T>(T value, SqlWindow window) => throw NotTranslated();

    public static int CountOver(SqlWindow window) => throw NotTranslated();

    public static int RowNumber(SqlWindow window) => throw NotTranslated();

    /// <summary>NTILE(buckets) OVER (ORDER BY order): the bucket (1..buckets) of the row, the first ones a row larger when the rows do not divide; <paramref name="buckets" /> a constant.</summary>
    public static int Ntile<TOrder>(int buckets, TOrder order) => throw NotTranslated();

    public static int Ntile<TPartition, TOrder>(int buckets, TPartition partition, TOrder order) => throw NotTranslated();

    /// <summary>FIRST_VALUE(value) OVER (ORDER BY order): the value of the ordering's first row.</summary>
    public static T FirstValue<T, TOrder>(T value, TOrder order) => throw NotTranslated();

    public static T FirstValue<T, TPartition, TOrder>(T value, TPartition partition, TOrder order) => throw NotTranslated();

    /// <summary>LAST_VALUE(value) over the whole partition (ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING): the ordering's last row, not the current one.</summary>
    public static T LastValue<T, TOrder>(T value, TOrder order) => throw NotTranslated();

    public static T LastValue<T, TPartition, TOrder>(T value, TPartition partition, TOrder order) => throw NotTranslated();

    // Casts

    /// <summary>CAST to text: TEXT, text, nvarchar(max).</summary>
    public static string ToText<T>(T value) => throw NotTranslated();

    /// <summary>CAST to a 32-bit integer: INTEGER, integer, int.</summary>
    public static int ToInt32(string value) => throw NotTranslated();

    /// <summary>CAST to a decimal: REAL on SQLite (which has no decimal), numeric, decimal(18, 2).</summary>
    public static decimal ToDecimal(string value) => throw NotTranslated();

    /// <summary>CAST to a double: REAL, double precision, float.</summary>
    public static double ToDouble(string value) => throw NotTranslated();

    /// <summary>The date as ISO text, <c>YYYY-MM-DD</c>: strftime, to_char, CONVERT(…, 23).</summary>
    public static string DateText(DateTimeOffset value) => throw NotTranslated();

    public static string DateText(DateTime value) => throw NotTranslated();

    private static NotSupportedException NotTranslated() => new("Sql.* functions are translated to SQL inside a query's lambda; they do not run in .NET.");
}
