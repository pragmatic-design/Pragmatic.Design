using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Pragmatic.Persistence.Sql.Writing;

/// <summary>One query of a batch: its <see cref="Result" /> is there once the batch has run.</summary>
public abstract class SqlBatchItem<TResult>
{
    private TResult? _result;
    private bool _ready;

    public TResult Result => _ready ? _result! : throw new InvalidOperationException("The batch has not run: await batch.ExecuteAsync() before reading a result.");

    public bool HasResult => _ready;

    private protected void Set(TResult result)
    {
        _result = result;
        _ready = true;
    }

    /// <summary>The item with its row type: the plan, the chain carrying the values, and how the plan reads its result sets.</summary>
    internal sealed class Entry<T> : SqlBatchItem<TResult>, ISqlBatchItem
    {
        private readonly SqlPlan<T> _plan;
        private readonly SqlQuery<T> _query;
        private readonly SqlBatchRead<T, TResult> _read;
        private readonly bool _counted;

        public Entry(SqlPlan<T> plan, in SqlQuery<T> query, SqlBatchRead<T, TResult> read, bool counted)
        {
            _plan = plan;
            _query = query;
            _read = read;
            _counted = counted;
        }

        public int PlanId => _plan.Id;

        public SqlParamKind[] Kinds => _plan.Parameters;

        public bool SingleStatement => _plan.Load is null && !_counted; // a counted command is two statements on SQL Server and MySQL: the batch keeps the joined text

        public string Sql(SqlDialect dialect) => _counted ? SqlWriteBatch.CountForm(_plan.Sql(dialect), dialect) : _plan.Sql(dialect);

        public void Bind(DbParameterCollection target, int offset, SqlDialect dialect)
        {
            // The plan binds into its own prepared command (the session keeps one per plan); the values are copied over, converted already.
            var bound = _plan.BoundCommand(_query);
            for (var i = 0; i < Kinds.Length; i++)
                target[offset + i].Value = bound.Parameters[i].Value;
        }

        public async Task ReadAsync(DbDataReader reader, SqlDialect dialect, CancellationToken ct) => Set(await _read(_plan, _query, reader, dialect, ct).ConfigureAwait(false));
    }
}
