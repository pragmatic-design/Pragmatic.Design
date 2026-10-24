using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Pragmatic.Persistence.Sql.Writing;

/// <summary>
///     Several declared queries in one command: each <c>[Query]</c> has a generated overload on the batch that records
///     the call (its plan and its values) and returns a typed item; <see cref="ExecuteAsync" /> joins the plans'
///     statements with their parameters renumbered, runs the command once, and reads each item's result sets in order
///     (a collection include's companions included). The command is prepared once per shape (the sequence of plans)
///     and kept with the session, so a page that asks a list and a count pays one round trip and binds values only.
/// </summary>
public sealed class SqlBatch
{
    private static readonly Regex ParameterName = new(@"@p(\d+)", RegexOptions.Compiled);
    private readonly List<ISqlBatchItem> _items = [];

    internal SqlBatch(SqlSession session) => Session = session;

    public SqlSession Session { get; }

    public int Count => _items.Count;

    /// <summary>
    ///     Records a plan and the chain carrying its values (what the generated batch overloads call); the item answers after <see cref="ExecuteAsync" />.
    ///     A <paramref name="counted" /> plan is a command: it joins in the form that answers its rows touched as a result set (the reader's
    ///     RecordsAffected sums every statement of the text).
    /// </summary>
    public SqlBatchItem<TResult> Add<T, TResult>(SqlPlan<T> plan, in SqlQuery<T> query, SqlBatchRead<T, TResult> read, bool counted = false)
    {
        if (plan.HasLists && Session.Dialect != SqlDialect.PostgreSql)
            throw new NotSupportedException("A query with an IN list expands its statement per call: it cannot join a batch, whose command is prepared once. Run it on its own (on PostgreSQL the list is an array parameter, and the query joins).");
        // INVESTIGATE: a batch could take the caller's variant and its value parameters after the batch's; refused until it does.
        if (plan.Filtered)
            throw new NotSupportedException("A query over Pragmatic's filtered entities has a statement per caller (which filters apply): it cannot join a batch yet. Run it on its own.");
        if (counted && plan.Sql(Session.Dialect).Contains("; ", StringComparison.Ordinal))
            throw new NotSupportedException("This command is several statements on this database: its rows touched cannot be counted inside a batch. Run it on its own.");
        var item = new SqlBatchItem<TResult>.Entry<T>(plan, query, read, counted);
        _items.Add(item);
        return item;
    }

    /// <summary>Runs the recorded queries in one command and fills every item's result.</summary>
    public async ValueTask ExecuteAsync(CancellationToken ct = default)
    {
        if (_items.Count == 0)
            return;
        var dialect = Session.Dialect;
        // Pipelined: the provider's batch, one command per plan with its own parameters; otherwise one text, the parameters renumbered.
        // A plan with companion statements (a collection include) is several statements sharing parameters: Npgsql takes one per batch command, so such a batch keeps the joined text.
        var pipeline = Session.Pipelining && _items.All(i => i.SingleStatement) ? Session.PipelineBatch(Key(), d => _items.Select(i => (i.Sql(d), i.Kinds)).ToList()) : null;
        DbDataReader reader;
        if (pipeline is not null)
        {
            for (var i = 0; i < _items.Count; i++)
                _items[i].Bind(pipeline.BatchCommands[i].Parameters, 0, dialect);
            reader = await Session.ReadAsync(pipeline, "batch", ct).ConfigureAwait(false);
        }
        else
        {
            var command = Session.BatchCommand(Key(), Build);
            var offset = 0;
            foreach (var item in _items)
            {
                item.Bind(command.Parameters, offset, dialect);
                offset += item.Kinds.Length;
            }

            reader = await Session.ReadAsync(command, "batch", ct).ConfigureAwait(false);
        }

        using (reader)
        {
            for (var i = 0; i < _items.Count; i++)
            {
                if (i > 0)
                    await reader.NextResultAsync(ct).ConfigureAwait(false);
                await _items[i].ReadAsync(reader, dialect, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>The command text the batch runs on its session's dialect: the plans' statements joined, their parameters renumbered.</summary>
    public string ToSql() => Session.Dialect == SqlDialect.Oracle ? SqlOracle.Text(Build(Session.Dialect).Sql) : Build(Session.Dialect).Sql; // as the database runs it: Oracle's is the PL/SQL block

    /// <summary>The shape of the batch: its plans in order. Two batches of the same shape share one prepared command.</summary>
    private string Key()
    {
        var key = new StringBuilder(_items.Count * 4);
        foreach (var item in _items)
            key.Append(item.PlanId).Append(',');
        return key.ToString();
    }

    /// <summary>The joined statements, each plan's parameters renumbered after the ones before, and the kinds in that order.</summary>
    private (string Sql, SqlParamKind[] Kinds) Build(SqlDialect dialect)
    {
        var sql = new StringBuilder();
        var kinds = new List<SqlParamKind>();
        var offset = 0;
        foreach (var item in _items)
        {
            if (sql.Length > 0)
                sql.Append("; ");
            var shift = offset;
            sql.Append(ParameterName.Replace(item.Sql(dialect), m => "@p" + (shift + int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture)));
            kinds.AddRange(item.Kinds);
            offset += item.Kinds.Length;
        }

        return (sql.ToString(), kinds.ToArray());
    }
}
