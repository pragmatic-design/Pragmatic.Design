using System.Collections;
using System.Globalization;
using System.Text;

namespace Pragmatic.Persistence.Sql.Planning;

/// <summary>
///     A predicate translated at compile time, for every dialect: the text on the row alias <c>t</c>, its values as tokens
///     <c>{v:i}</c> (an IN list's token expands to one parameter per item when the predicate is rendered, an array on PostgreSQL).
///     A leaf of <see cref="SqlPredicate{T}" />: the generator emits one per predicate written in source (a specification's
///     <c>Spec&lt;T&gt;.Where</c>, a declared query's filter), and the runtime composes them without reading any text from the caller.
/// </summary>
public sealed class SqlCompiledPredicate(string sqlite, string postgreSql, string sqlServer, string? mySql, string? oracle, SqlParamKind[] kinds)
{
    private readonly string[] _texts = [sqlite, postgreSql, sqlServer, mySql ?? "", oracle ?? ""];

    /// <summary>The kind of each value, by token index.</summary>
    public SqlParamKind[] Kinds { get; } = kinds;

    /// <summary>The text for a dialect; empty where the predicate has no form on it.</summary>
    public string Text(SqlDialect dialect) => _texts[(int)dialect];
}
