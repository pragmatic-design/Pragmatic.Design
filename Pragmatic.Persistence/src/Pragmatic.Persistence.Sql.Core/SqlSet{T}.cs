using System.Collections;
using System.Data.Common;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Pragmatic.Persistence.Sql;

/// <summary>The SET list of <see cref="SqlQuery{T}.ExecuteUpdateAsync" />, as the developer writes it: read by the generator, never run.</summary>
public sealed class SqlSet<T>
{
    /// <summary>The column takes a value (a parameter).</summary>
    public SqlSet<T> SetProperty<TValue>(Func<T, TValue> property, TValue value) => this;

    /// <summary>The column takes an expression over the row (<c>o => o.Total * 2</c>), translated; no captured values in it.</summary>
    public SqlSet<T> SetProperty<TValue>(Func<T, TValue> property, Func<T, TValue> expression) => this;
}
