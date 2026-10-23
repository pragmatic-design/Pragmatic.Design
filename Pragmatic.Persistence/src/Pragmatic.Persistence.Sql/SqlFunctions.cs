using System.Collections.Concurrent;
using System.Reflection;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     On a <c>static partial</c> method of a <c>static partial</c> class: a SQL function of the application, usable in a
///     query's lambdas like the catalog's. The generator implements the method (it throws: never run in .NET) and reads its
///     rendering from a private static method of the same name whose first parameter is the dialect, the others
///     <see cref="SqlFragment" />s, and whose body is a <c>switch</c> on the dialect built with <see cref="Fragment" />:
///     <code>
///     [SqlFunction] public static partial double DaysBetween(DateTimeOffset from, DateTimeOffset to);
///     private static SqlFragment DaysBetween(SqlDialect dialect, SqlFragment from, SqlFragment to) => dialect switch
///     {
///         SqlDialect.SqlServer => Fragment.Call("DATEDIFF", Fragment.Keyword("day"), from, to),
///         SqlDialect.PostgreSql => Fragment.Extract(DateField.Epoch, to - from) / 86400,
///         SqlDialect.Sqlite => Fragment.Call("julianday", to) - Fragment.Call("julianday", from),
///     };
///     </code>
///     The definition is read, never executed; a dialect without an arm is an error where the function is used (PRAG3013).
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SqlFunctionAttribute : Attribute;

/// <summary>What <c>EXTRACT</c> reads from a date.</summary>
public enum DateField
{
    Year,
    Month,
    Day,
    Hour,
    Minute,
    Second,
    Epoch,
    DayOfWeek,
}

/// <summary>A SQL type to cast to, named per dialect by the generator.</summary>
public enum SqlType
{
    Integer,
    BigInt,
    Numeric,
    Double,
    Text,
}

/// <summary>
///     A piece of SQL inside an application function's definition: an argument, a call, a literal, an expression over
///     them. Only read by the generator; the operators exist for the definition to type-check, and throw if ever run.
/// </summary>
public readonly struct SqlFragment
{
    public static SqlFragment operator +(SqlFragment left, SqlFragment right) => throw Fragment.NotRun();

    public static SqlFragment operator -(SqlFragment left, SqlFragment right) => throw Fragment.NotRun();

    public static SqlFragment operator *(SqlFragment left, SqlFragment right) => throw Fragment.NotRun();

    public static SqlFragment operator /(SqlFragment left, SqlFragment right) => throw Fragment.NotRun();

    public static SqlFragment operator %(SqlFragment left, SqlFragment right) => throw Fragment.NotRun();

    public static SqlFragment operator >(SqlFragment left, SqlFragment right) => throw Fragment.NotRun();

    public static SqlFragment operator <(SqlFragment left, SqlFragment right) => throw Fragment.NotRun();

    public static SqlFragment operator >=(SqlFragment left, SqlFragment right) => throw Fragment.NotRun();

    public static SqlFragment operator <=(SqlFragment left, SqlFragment right) => throw Fragment.NotRun();

    /// <summary>A number as a SQL literal.</summary>
    public static implicit operator SqlFragment(int value) => throw Fragment.NotRun();

    public static implicit operator SqlFragment(long value) => throw Fragment.NotRun();

    public static implicit operator SqlFragment(decimal value) => throw Fragment.NotRun();

    public static implicit operator SqlFragment(double value) => throw Fragment.NotRun();
}

/// <summary>The combinators an application function's definition is written with. Read by the generator, never run.</summary>
public static class Fragment
{
    /// <summary>A call of a SQL function by name: <c>NAME(args)</c>.</summary>
    public static SqlFragment Call(string function, params SqlFragment[] arguments) => throw NotRun();

    /// <summary>A keyword written as it is (<c>day</c> in DATEDIFF, <c>DISTINCT</c>).</summary>
    public static SqlFragment Keyword(string keyword) => throw NotRun();

    /// <summary>A string as a SQL literal, quotes doubled.</summary>
    public static SqlFragment Literal(string value) => throw NotRun();

    /// <summary><c>EXTRACT(FIELD FROM value)</c>.</summary>
    public static SqlFragment Extract(DateField field, SqlFragment value) => throw NotRun();

    /// <summary><c>CAST(value AS type)</c>, the type named per dialect.</summary>
    public static SqlFragment Cast(SqlFragment value, SqlType type) => throw NotRun();

    /// <summary><c>(left AND right)</c>.</summary>
    public static SqlFragment And(SqlFragment left, SqlFragment right) => throw NotRun();

    /// <summary><c>(left OR right)</c>.</summary>
    public static SqlFragment Or(SqlFragment left, SqlFragment right) => throw NotRun();

    /// <summary><c>left = right</c>.</summary>
    public static SqlFragment Equal(SqlFragment left, SqlFragment right) => throw NotRun();

    /// <summary>Marks a dialect's rendering as an emulation: the generator reports the note where the function is used (PRAG3012).</summary>
    public static SqlFragment Emulated(SqlFragment fragment, string note) => throw NotRun();

    internal static NotSupportedException NotRun() => new("A SqlFragment is read by the generator inside a [SqlFunction] definition; it never runs in .NET.");
}

/// <summary>
///     The application functions the generator registered for the runtime path, by method: a renderer per dialect over the
///     arguments' SQL. Registered at module start from the definitions the generator read, like the entity maps.
/// </summary>
public static class SqlFunctions
{
    private static readonly ConcurrentDictionary<MethodInfo, (Func<SqlDialect, string[], string> Render, bool IsPredicate)> s_functions = new();

    public static void Register(MethodInfo method, Func<SqlDialect, string[], string> render, bool isPredicate = false) => s_functions[method] = (render, isPredicate);

    public static bool TryGet(MethodInfo method, out Func<SqlDialect, string[], string> render, out bool isPredicate)
    {
        if (s_functions.TryGetValue(method, out var entry))
        {
            render = entry.Render;
            isPredicate = entry.IsPredicate;
            return true;
        }

        render = null!;
        isPredicate = false;
        return false;
    }
}
