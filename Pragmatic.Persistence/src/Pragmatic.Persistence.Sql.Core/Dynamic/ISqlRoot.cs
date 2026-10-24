using System.Collections;
using System.Linq.Expressions;

namespace Pragmatic.Persistence.Sql.Dynamic;

/// <summary>What a runtime chain starts from: the session, and the entity whose map describes the table.</summary>
internal interface ISqlRoot
{
    SqlSession Session { get; }

    Type EntityType { get; }

    /// <summary>The hand-written root statement, when the chain starts at <c>session.Sql&lt;T&gt;</c>.</summary>
    SqlRaw? Raw { get; }
}
