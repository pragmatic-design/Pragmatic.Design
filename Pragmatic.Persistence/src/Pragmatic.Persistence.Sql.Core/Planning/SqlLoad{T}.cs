using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Pragmatic.Persistence.Sql.Planning;

/// <summary>
///     Loads the collection includes from the result sets that follow the rows' own, in the same command: one
///     statement per collection, depth first, each selecting the children whose foreign key is in the parents'
///     keys (the parent statement as a subquery). One round trip whatever the depth.
/// </summary>
public delegate Task SqlLoad<T>(DbDataReader reader, SqlDialect dialect, List<T> rows, CancellationToken ct);
