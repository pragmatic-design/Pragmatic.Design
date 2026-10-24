using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Pragmatic.Persistence.Sql.Writing;

/// <summary>Reads one item's result set(s) off the batch's reader into the item's typed result (a plan's <c>Read…Async</c>).</summary>
public delegate ValueTask<TResult> SqlBatchRead<T, TResult>(SqlPlan<T> plan, SqlQuery<T> query, DbDataReader reader, SqlDialect dialect, CancellationToken ct);
