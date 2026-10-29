using System.Data;
using System.Data.Common;
using System.Globalization;

namespace Pragmatic.Persistence.Sql.Planning;

/// <summary>
///     How .NET values cross into each database and back. PostgreSQL and SQL Server have the types;
///     SQLite stores guids and dates as text, money as REAL and booleans as integers, so the readers the
///     generator emits call these instead of the raw <see cref="DbDataReader" /> getters.
/// </summary>
public static class SqlValue
{
    private const string SqliteDateFormat = "yyyy-MM-dd HH:mm:ss.fffffffzzz";
    private const string SqliteDateTimeFormat = "yyyy-MM-dd HH:mm:ss.fffffff";
    private const string SqliteDayFormat = "yyyy-MM-dd";
    private const string SqliteClockFormat = "HH:mm:ss.fffffff";

    /// <summary>A parameter value as the database takes it: a TimeSpan as its ticks everywhere; SQLite's text and integer stand-ins; PostgreSQL's timestamp wants an unspecified kind.</summary>
    public static object ToDatabase(SqlDialect dialect, object? value)
    {
        if (value is null)
            return DBNull.Value;
        if (value is SqlFullTextQuery fullText)
            return fullText.Condition(dialect) is { } condition ? condition : DBNull.Value; // every word required, in the engine's syntax
        if (value is Enum e)
            value = Convert.ToInt32(e, CultureInfo.InvariantCulture);
        if (value is TimeSpan span)
            return span.Ticks;
        if (value is float[] vector)
            return SqlVectors.Format(vector); // every dialect takes the JSON text: PostgreSQL and SQL Server cast it to their vector type in the statement
        if (dialect == SqlDialect.PostgreSql)
        {
            if (value is DateTime stamp)
                return System.DateTime.SpecifyKind(stamp, DateTimeKind.Unspecified); // a `timestamp` column: Npgsql refuses a UTC kind there
            if (value is System.Collections.IEnumerable list and not string and not byte[])
                return PostgreSqlArray(list); // a list binds as an array (= ANY): Npgsql maps a typed collection itself; what it does not know is converted
            return value;
        }
        if (dialect is SqlDialect.MySql or SqlDialect.Oracle && value is DateTimeOffset instant)
            return instant.UtcDateTime; // a DATETIME(6) / TIMESTAMP column holding the UTC instant, read back as UTC
        if (dialect == SqlDialect.Oracle)
        {
            // No uuid, no day-only or time-of-day type that every provider path agrees on: the text forms SQLite uses.
            return value switch
            {
                Guid g => g.ToString(),
                DateOnly day => day.ToString(SqliteDayFormat, CultureInfo.InvariantCulture),
                TimeOnly clock => clock.ToString(SqliteClockFormat, CultureInfo.InvariantCulture),
                _ => value,
            };
        }

        if (dialect != SqlDialect.Sqlite)
            return value;
        return value switch
        {
            decimal d => (double)d,
            DateTimeOffset t => t.ToString(SqliteDateFormat, CultureInfo.InvariantCulture),
            DateTime t => t.ToString(SqliteDateTimeFormat, CultureInfo.InvariantCulture),
            DateOnly day => day.ToString(SqliteDayFormat, CultureInfo.InvariantCulture),
            TimeOnly clock => clock.ToString(SqliteClockFormat, CultureInfo.InvariantCulture),
            Guid g => g.ToString(),
            bool b => b ? 1 : 0,
            _ => value,
        };
    }

    /// <summary>
    ///     A list as a PostgreSQL array parameter: enums as int[], TimeSpan as ticks (long[]), DateTime with the kind Npgsql takes for
    ///     <c>timestamp</c>; any other element type as the collection itself, whose static type Npgsql maps (a <c>List&lt;Guid&gt;</c> is uuid[]).
    /// </summary>
    private static object PostgreSqlArray(System.Collections.IEnumerable list)
    {
        object? first = null;
        foreach (var item in list)
        {
            first = item;
            break;
        }

        switch (first)
        {
            case Enum:
                return Collect(list, static item => Convert.ToInt32((Enum)item!, CultureInfo.InvariantCulture));
            case System.TimeSpan:
                return Collect(list, static item => ((System.TimeSpan)item!).Ticks);
            case System.DateTime:
                return Collect(list, static item => System.DateTime.SpecifyKind((System.DateTime)item!, DateTimeKind.Unspecified));
            default:
                return list;
        }
    }

    private static T[] Collect<T>(System.Collections.IEnumerable list, Func<object?, T> convert)
    {
        var items = new List<T>();
        foreach (var item in list)
            items.Add(convert(item));
        return items.ToArray();
    }

    public static DateTime DateTime(DbDataReader reader, int ordinal, SqlDialect dialect)
        => dialect == SqlDialect.Sqlite ? System.DateTime.ParseExact(reader.GetString(ordinal), SqliteDateTimeFormat, CultureInfo.InvariantCulture) : reader.GetDateTime(ordinal);

    public static DateTime? NullableDateTime(DbDataReader reader, int ordinal, SqlDialect dialect)
        => reader.IsDBNull(ordinal) ? null : DateTime(reader, ordinal, dialect);

    public static DateOnly DateOnly(DbDataReader reader, int ordinal, SqlDialect dialect)
        => dialect is SqlDialect.Sqlite or SqlDialect.Oracle ? System.DateOnly.ParseExact(reader.GetString(ordinal), SqliteDayFormat, CultureInfo.InvariantCulture) : reader.GetFieldValue<DateOnly>(ordinal);

    public static DateOnly? NullableDateOnly(DbDataReader reader, int ordinal, SqlDialect dialect)
        => reader.IsDBNull(ordinal) ? null : DateOnly(reader, ordinal, dialect);

    public static TimeOnly TimeOnly(DbDataReader reader, int ordinal, SqlDialect dialect)
        => dialect is SqlDialect.Sqlite or SqlDialect.Oracle ? System.TimeOnly.ParseExact(reader.GetString(ordinal), SqliteClockFormat, CultureInfo.InvariantCulture) : reader.GetFieldValue<TimeOnly>(ordinal);

    public static TimeOnly? NullableTimeOnly(DbDataReader reader, int ordinal, SqlDialect dialect)
        => reader.IsDBNull(ordinal) ? null : TimeOnly(reader, ordinal, dialect);

    /// <summary>A TimeSpan from the 64-bit ticks it is stored as, on every database (SQL Server's time stops at a day, PostgreSQL's interval is its own type: ticks are portable).</summary>
    public static TimeSpan TimeSpan(DbDataReader reader, int ordinal) => System.TimeSpan.FromTicks(reader.GetInt64(ordinal));

    public static TimeSpan? NullableTimeSpan(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : TimeSpan(reader, ordinal);

    /// <summary>A binary column; NULL reads as empty (Oracle stores an empty BLOB as NULL, as it does the empty string).</summary>
    public static byte[] Bytes(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? [] : reader.GetFieldValue<byte[]>(ordinal);

    public static byte[]? NullableBytes(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<byte[]>(ordinal);

    public static Guid Guid(DbDataReader reader, int ordinal, SqlDialect dialect)
        => SqlDialects.GuidsAsText(dialect) ? System.Guid.Parse(reader.GetString(ordinal)) : reader.GetGuid(ordinal);

    public static Guid? NullableGuid(DbDataReader reader, int ordinal, SqlDialect dialect)
        => reader.IsDBNull(ordinal) ? null : Guid(reader, ordinal, dialect);

    public static decimal Decimal(DbDataReader reader, int ordinal, SqlDialect dialect)
        => dialect == SqlDialect.Sqlite ? (decimal)reader.GetDouble(ordinal) : reader.GetDecimal(ordinal);

    public static decimal? NullableDecimal(DbDataReader reader, int ordinal, SqlDialect dialect)
        => reader.IsDBNull(ordinal) ? null : Decimal(reader, ordinal, dialect);

    public static DateTimeOffset DateTimeOffset(DbDataReader reader, int ordinal, SqlDialect dialect) => dialect switch
    {
        SqlDialect.Sqlite => System.DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture),
        SqlDialect.PostgreSql or SqlDialect.MySql or SqlDialect.Oracle => new DateTimeOffset(System.DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc)), // timestamptz comes back as a UTC DateTime; MySQL's DATETIME and Oracle's TIMESTAMP hold the UTC instant
        _ => reader.GetFieldValue<DateTimeOffset>(ordinal),
    };

    public static DateTimeOffset? NullableDateTimeOffset(DbDataReader reader, int ordinal, SqlDialect dialect)
        => reader.IsDBNull(ordinal) ? null : DateTimeOffset(reader, ordinal, dialect);

    public static bool Boolean(DbDataReader reader, int ordinal, SqlDialect dialect)
        => dialect == SqlDialect.Sqlite ? reader.GetInt64(ordinal) != 0 : reader.GetBoolean(ordinal);

    public static bool? NullableBoolean(DbDataReader reader, int ordinal, SqlDialect dialect)
        => reader.IsDBNull(ordinal) ? null : Boolean(reader, ordinal, dialect);

    /// <summary>A text column; NULL reads as "" (Oracle stores the empty string as NULL: what was written comes back; elsewhere a NULL in a non-nullable member reads empty too).</summary>
    public static string String(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);

    public static string? NullableString(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    /// <summary>An <c>[Interned]</c> column: the pooled instance of the value (see <see cref="SqlInternPool" />).</summary>
    public static string Interned(DbDataReader reader, int ordinal, SqlDialect dialect, SqlInternPool pool) => pool.Read(reader, ordinal, dialect);

    public static string? NullableInterned(DbDataReader reader, int ordinal, SqlDialect dialect, SqlInternPool pool) => reader.IsDBNull(ordinal) ? null : pool.Read(reader, ordinal, dialect);

    public static int Int32(DbDataReader reader, int ordinal) => reader.GetInt32(ordinal);

    public static int? NullableInt32(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    public static long Int64(DbDataReader reader, int ordinal) => reader.GetInt64(ordinal);

    public static long? NullableInt64(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    public static double Double(DbDataReader reader, int ordinal) => reader.GetDouble(ordinal);

    /// <summary>
    ///     Types a parameter once, so the command can be prepared: the database's type for the value, as the schema
    ///     the generator writes declares it (SQLite: guids as text, money as REAL, booleans and dates as text/integers).
    ///     Sizes are the ones SqlClient needs to prepare a variable-length parameter; a decimal is (18,2) until
    ///     [Column] carries precision and scale (ASSUMPTION: the workload's schema).
    /// </summary>
    public static void Configure(DbParameter parameter, SqlParamKind kind, SqlDialect dialect)
    {
        switch (kind)
        {
            case SqlParamKind.Guid when SqlDialects.GuidsAsText(dialect):
                parameter.DbType = DbType.String;
                parameter.Size = 36;
                break;
            case SqlParamKind.Guid:
                parameter.DbType = DbType.Guid;
                break;
            case SqlParamKind.String:
                parameter.DbType = DbType.String;
                parameter.Size = 4000;
                break;
            case SqlParamKind.Int32:
                parameter.DbType = DbType.Int32;
                break;
            case SqlParamKind.Int64:
                parameter.DbType = DbType.Int64;
                break;
            case SqlParamKind.Decimal when dialect == SqlDialect.Sqlite:
                parameter.DbType = DbType.Double;
                break;
            case SqlParamKind.Decimal:
                parameter.DbType = DbType.Decimal;
                parameter.Precision = 18;
                parameter.Scale = 2;
                break;
            case SqlParamKind.Double:
                parameter.DbType = DbType.Double;
                break;
            case SqlParamKind.Boolean:
                parameter.DbType = dialect == SqlDialect.Sqlite ? DbType.Int32 : DbType.Boolean;
                break;
            case SqlParamKind.DateTimeOffset when dialect == SqlDialect.Sqlite:
                parameter.DbType = DbType.String;
                parameter.Size = 40;
                break;
            case SqlParamKind.DateTimeOffset when dialect is SqlDialect.MySql or SqlDialect.Oracle:
                parameter.DbType = DbType.DateTime; // the UTC instant as a DATETIME(6) / TIMESTAMP
                break;
            case SqlParamKind.DateTimeOffset:
                parameter.DbType = DbType.DateTimeOffset;
                parameter.Scale = 7; // SqlClient treats datetimeoffset as variable-length: Prepare() wants the fraction digits and a size
                parameter.Size = 10;
                break;
            case SqlParamKind.DateTime when dialect == SqlDialect.Sqlite:
                parameter.DbType = DbType.String;
                parameter.Size = 27;
                break;
            case SqlParamKind.DateTime when dialect == SqlDialect.Oracle:
                parameter.DbType = DbType.DateTime; // ODP.NET has no DateTime2: a TIMESTAMP
                break;
            case SqlParamKind.DateTime:
                parameter.DbType = DbType.DateTime2; // datetime2 on SQL Server, timestamp (without time zone) on PostgreSQL
                parameter.Scale = 7;
                parameter.Size = 8;
                break;
            case SqlParamKind.Bytes when dialect == SqlDialect.Oracle:
                parameter.DbType = DbType.Binary; // a BLOB: the size from the value
                break;
            case SqlParamKind.Bytes:
                parameter.DbType = DbType.Binary;
                parameter.Size = -1; // varbinary(max): SqlClient prepares a variable-length parameter only with a size
                break;
            case SqlParamKind.DateOnly when dialect is SqlDialect.Sqlite or SqlDialect.Oracle:
                parameter.DbType = DbType.String;
                parameter.Size = 10;
                break;
            case SqlParamKind.DateOnly:
                parameter.DbType = DbType.Date;
                break;
            case SqlParamKind.TimeOnly when dialect is SqlDialect.Sqlite or SqlDialect.Oracle:
                parameter.DbType = DbType.String;
                parameter.Size = 16;
                break;
            case SqlParamKind.TimeOnly:
                parameter.DbType = DbType.Time;
                parameter.Scale = 7;
                parameter.Size = 5;
                break;
            case SqlParamKind.Vector when dialect == SqlDialect.Oracle:
                parameter.DbType = DbType.String; // the JSON text, converted by TO_VECTOR / the column
                parameter.Size = 4000;
                break;
            case SqlParamKind.Vector:
                parameter.DbType = DbType.String; // the JSON text; the statement casts (CAST(@p AS vector), CAST(@p AS VECTOR(n)))
                parameter.Size = -1;
                break;
            case SqlParamKind.Array when dialect != SqlDialect.PostgreSql:
                parameter.DbType = DbType.String; // the JSON text
                parameter.Size = dialect == SqlDialect.Oracle ? 4000 : -1;
                break;
            case SqlParamKind.Array:
            case SqlParamKind.List:
            case SqlParamKind.Unknown:
                break; // bound untyped: the provider infers from the value
        }
    }

    /// <summary>
    ///     A vector column as floats: through the provider's registered reader when there is one (SqlClient's <c>SqlVector&lt;float&gt;</c>), else as the
    ///     text the database answers (pgvector's <c>[1,2,3]</c> read as a string, Npgsql knowing no vector type without its plugin; SQLite's stored text).
    /// </summary>
    public static float[] Vector(DbDataReader reader, int ordinal, SqlDialect dialect)
    {
        var value = reader.GetValue(ordinal); // the generated select lists read a vector as text (CAST per dialect): a string here
        if (value is string text)
            return SqlVectors.Parse(text);
        if (value is float[] floats)
            return floats;
        if (SqlVectors.ReaderFor(dialect) is { } read)
            return read(reader, ordinal); // a provider's own type (SqlClient's SqlVector<float>), when a statement selected the column raw
        return SqlVectors.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
    }

    public static float[]? NullableVector(DbDataReader reader, int ordinal, SqlDialect dialect) => reader.IsDBNull(ordinal) ? null : Vector(reader, ordinal, dialect);

    /// <summary>The kind a runtime value is bound as, so a dynamic query's command can be typed and prepared like a generated one.</summary>
    public static SqlParamKind KindOf(object? value) => value switch
    {
        null => SqlParamKind.Unknown,
        System.Guid => SqlParamKind.Guid, // qualified: inside this class the bare names bind to the reader methods
        string => SqlParamKind.String,
        int or Enum => SqlParamKind.Int32,
        long => SqlParamKind.Int64,
        decimal => SqlParamKind.Decimal,
        double => SqlParamKind.Double,
        bool => SqlParamKind.Boolean,
        System.DateTimeOffset => SqlParamKind.DateTimeOffset,
        System.DateTime => SqlParamKind.DateTime,
        System.DateOnly => SqlParamKind.DateOnly,
        System.TimeOnly => SqlParamKind.TimeOnly,
        System.TimeSpan => SqlParamKind.Int64, // bound as ticks
        byte[] => SqlParamKind.Bytes,
        float[] => SqlParamKind.Vector,
        System.Collections.IEnumerable => SqlParamKind.List,
        _ => SqlParamKind.Unknown,
    };

    /// <summary>A nullable value through its converter, null staying null (the generated binders of converted columns).</summary>
    public static object? Converted<TModel, TStore, TConverter>(TModel? value)
        where TModel : struct
        where TConverter : ISqlConverter<TModel, TStore>
        => value is null ? null : TConverter.ToStore(value.Value);

    public static object? ConvertedRef<TModel, TStore, TConverter>(TModel? value)
        where TModel : class
        where TConverter : ISqlConverter<TModel, TStore>
        => value is null ? null : TConverter.ToStore(value);

    /// <summary>A COUNT(*) column in a grouped row: 64-bit on SQLite and PostgreSQL, 32-bit on SQL Server.</summary>
    public static int Count(DbDataReader reader, int ordinal, SqlDialect dialect) => Count(reader.GetValue(ordinal));

    /// <summary>An AVG result, whatever numeric type the database chose for it (SQL Server keeps the column's, PostgreSQL answers numeric).</summary>
    public static double Average(DbDataReader reader, int ordinal, SqlDialect dialect)
        => dialect == SqlDialect.Oracle ? reader.GetDouble(ordinal) : Convert.ToDouble(reader.GetValue(ordinal), CultureInfo.InvariantCulture); // Oracle's AVG is a NUMBER of 38 digits: past decimal's range as an object

    /// <summary>A COUNT(*) result: SQLite and PostgreSQL answer a 64-bit integer, SQL Server a 32-bit one.</summary>
    public static int Count(object? scalar) => scalar switch
    {
        int i => i,
        long l => checked((int)l),
        decimal d => (int)d,
        null => 0,
        _ => Convert.ToInt32(scalar, CultureInfo.InvariantCulture),
    };
}
