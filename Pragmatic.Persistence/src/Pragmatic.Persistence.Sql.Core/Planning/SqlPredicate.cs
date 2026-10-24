using System.Collections;
using System.Globalization;
using System.Text;

namespace Pragmatic.Persistence.Sql.Planning;

/// <summary>
///     A predicate over an entity's rows composed at runtime from compiled leaves (<see cref="SqlCompiledPredicate" />): AND, OR, NOT
///     and TRUE. Rendered into a statement where the plan marks it (<c>Matching(predicate)</c> on a chain): the text is assembled from the
///     leaves' compiled texts, the values become parameters after the plan's own. The leaves are validated at compile time, the composition
///     is a shape the runtime renders (parenthesised, so precedence is the tree's).
/// </summary>
public abstract class SqlPredicate : SqlComposed
{
    private protected SqlPredicate()
    {
    }
}
