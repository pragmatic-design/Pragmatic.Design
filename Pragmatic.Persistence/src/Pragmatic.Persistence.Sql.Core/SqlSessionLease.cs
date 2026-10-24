using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;

namespace Pragmatic.Persistence.Sql;

/// <summary>A session rented from a <see cref="SqlSessionPool" />: disposing it returns the session (or disposes it, when it cannot be reused).</summary>
public readonly struct SqlSessionLease(SqlSessionPool pool, SqlSession session) : IDisposable
{
    public SqlSession Session => session;

    public void Dispose() => pool.Return(session);
}
