using System.Collections.Concurrent;
using System.Reflection;

namespace Pragmatic.Persistence.Sql.Attributes;

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
