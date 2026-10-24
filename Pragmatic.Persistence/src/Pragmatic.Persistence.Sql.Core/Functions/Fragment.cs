using System.Collections.Concurrent;
using System.Reflection;

namespace Pragmatic.Persistence.Sql.Functions;

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
