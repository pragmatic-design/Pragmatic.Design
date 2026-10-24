using System.Collections;
using System.Globalization;
using System.Text;

namespace Pragmatic.Persistence.Sql.Planning;

/// <summary>
///     A piece of a statement composed at runtime from parts compiled at compile time (a predicate, an ordering): the plan marks where it
///     goes (<c>{dyn:k}</c> for the chain's slot <c>k</c>), the runtime renders it there when the statement runs.
/// </summary>
public abstract class SqlComposed
{
    private protected SqlComposed()
    {
    }

    /// <summary>
    ///     The text for <paramref name="dialect" /> on the row alias <paramref name="alias" />, its values appended to
    ///     <paramref name="values" /> as parameters named from <paramref name="firstParameter" /> on.
    /// </summary>
    public abstract void Render(SqlDialect dialect, string alias, StringBuilder text, List<(SqlParamKind Kind, object? Value)> values, int firstParameter);
}
