using System.Collections.Concurrent;
using System.Linq.Expressions;
using Pragmatic.Persistence.Expressions;

namespace Pragmatic.Persistence.Sql.Dynamic;

/// <summary>How a captured sub-tree becomes a parameter value: as it is, as a LIKE pattern, as an IN list, the constant true a bare boolean column compares to, or a literal kept with the shape (a collection include's filter).</summary>
internal enum SqlCaptureKind
{
    Value,
    Like,
    List,
    True,
    Constant,
}
