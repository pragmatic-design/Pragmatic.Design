using System.Data.Common;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     Pipelining: where the provider has <see cref="DbBatch" /> (Npgsql, SqlClient; not Microsoft.Data.Sqlite), a batch of statements goes
///     as one batch of commands, each with its own text and parameters, instead of one text joined with <c>;</c> and the parameters
///     renumbered. The provider sends the commands in one round trip either way. Measured (BatchBenchmarks, a page and a count):
///     on PostgreSQL the same time and memory as the joined text (1,15 against 1,17 ms, 6,5 KB both); on SQL Server slower and
///     noisier (2,6 ± 0,8 against 1,6 ms, 10,7 against 8,7 KB). Off by default therefore: <see cref="Pipelining" /> opts in, for a
///     provider or a workload where the measurement says otherwise.
/// </summary>
public sealed partial class SqlSession
{
    private Dictionary<string, DbBatch>? _pipelines; // the provider batches, by the sequence of their statements' plans

    /// <summary>True when batches run as the provider's own <see cref="DbBatch" />; off by default (see the measurement above), and without effect where the provider cannot create batches.</summary>
    public bool Pipelining { get; set; }

    /// <summary>The provider batch of a shape, built the first time with <paramref name="build" /> (one text and its parameter kinds per command) and kept; null when the provider cannot build it.</summary>
    internal DbBatch? PipelineBatch(string key, Func<SqlDialect, List<(string Sql, SqlParamKind[] Kinds)>> build)
    {
        _pipelines ??= new Dictionary<string, DbBatch>(StringComparer.Ordinal);
        if (!_pipelines.TryGetValue(key, out var batch))
        {
            var commands = build(Dialect);
            batch = FreshPipeline(commands, out var preparable);
            if (batch is null)
                return null;
            if (preparable && commands.All(c => Prepares(c.Sql)))
                batch.Prepare();
            _pipelines[key] = batch;
        }

        batch.Transaction = Transaction;
        return batch;
    }

    /// <summary>A provider batch built for one execution and not kept (a statement whose text varies per call).</summary>
    internal DbBatch? FreshPipeline(List<(string Sql, SqlParamKind[] Kinds)> commands, out bool preparable)
    {
        preparable = true;
        if (!Connection.CanCreateBatch)
            return null;
        var batch = Connection.CreateBatch();
        foreach (var (sql, kinds) in commands)
        {
            var command = batch.CreateBatchCommand();
            if (!command.CanCreateParameter)
            {
                batch.Dispose();
                return null;
            }

            command.CommandText = Qualify(sql);
            for (var i = 0; i < kinds.Length; i++)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@p" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                SqlValue.Configure(parameter, kinds[i], Dialect);
                command.Parameters.Add(parameter);
                preparable &= kinds[i] is not (SqlParamKind.Unknown or SqlParamKind.List) && (kinds[i] != SqlParamKind.Array || Dialect != SqlDialect.PostgreSql);
            }

            if (Dialect == SqlDialect.Oracle)
                SqlOracle.Adapt(command);
            batch.BatchCommands.Add(command);
        }

        if (CommandTimeout is { } timeout)
            batch.Timeout = timeout;
        batch.Transaction = Transaction;
        return batch;
    }

    private void DisposePipelines()
    {
        if (_pipelines is null)
            return;
        foreach (var batch in _pipelines.Values)
            batch.Dispose();
        _pipelines = null;
    }
}
