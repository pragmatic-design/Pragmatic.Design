using System.Collections.Concurrent;
using System.Reflection;

namespace Pragmatic.Persistence.Sql.Functions;

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
