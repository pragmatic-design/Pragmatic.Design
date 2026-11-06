using System.Data.Common;
using System.Globalization;
using Pragmatic.Persistence.Sql.Planning;
using Pragmatic.Persistence.Sql.Writing;

namespace Pragmatic.Persistence.Sql;

/// <summary>The next values of a database sequence (see <see cref="SqlSequences" />), read through the session's connection and transaction, the command prepared once per sequence.</summary>
public sealed partial class SqlSession
{
    private const string SequenceSavepoint = "pragmatic_sequence";
    private HashSet<string>? _ensuredSequences; // the sequences this session has seen there: asked once per session

    /// <summary>
    ///     Creates the sequence when it is not there, as EF Core's <c>SequenceValueProvider</c> does on a first use: what an <c>[GeneratedValue]</c>
    ///     <c>{SEQ}</c> number reads on the engines, whose schema declares no sequence (#562).
    /// </summary>
    /// <remarks>
    ///     <c>IF NOT EXISTS</c> is not free of races: two sessions on a first use both decide to create, and the loser fails on the catalogue. The
    ///     post-condition is checked, not the error: a failed create is followed by asking whether the sequence is there now, and only when it is
    ///     not does the failure stand. Inside the caller's transaction the create runs in a savepoint, so the failure does not abort it
    ///     (PostgreSQL's 25P02). On SQLite and MySQL the sequence is a row of a counter table, created with <c>INSERT … IGNORE</c>: no race.
    /// </remarks>
    public async ValueTask EnsureSequenceAsync(string sequence, CancellationToken ct = default)
    {
        SqlSequences.Validate(sequence);
        if (_ensuredSequences?.Contains(sequence) == true)
            return;

        if (SqlSequences.IsEmulated(Dialect))
        {
            foreach (var statement in SqlSequences.CreateSql(Dialect, sequence))
                await RunAsync(FreshCommand(statement, []), "sequence " + sequence, ct).ConfigureAwait(false);
        }
        else if (!await SequenceExistsAsync(sequence, ct).ConfigureAwait(false))
        {
            var savepoint = Transaction is not null;
            if (savepoint)
                await SavepointAsync(SequenceSavepoint, ct).ConfigureAwait(false);
            try
            {
                foreach (var statement in SqlSequences.CreateSql(Dialect, sequence))
                    await RunAsync(FreshCommand(statement, []), "sequence " + sequence, ct).ConfigureAwait(false);
            }
            catch (DbException)
            {
                if (savepoint)
                    await RollbackToSavepointAsync(SequenceSavepoint, CancellationToken.None).ConfigureAwait(false);
                if (!await SequenceExistsAsync(sequence, ct).ConfigureAwait(false))
                    throw; // not the race: a missing privilege, a server gone
            }
        }

        (_ensuredSequences ??= new HashSet<string>(StringComparer.Ordinal)).Add(sequence);
    }

    private async Task<bool> SequenceExistsAsync(string sequence, CancellationToken ct)
    {
        var command = FreshCommand(SqlSequences.ExistsSql(Dialect), [SqlParamKind.String]);
        command.Parameters[0].Value = SqlSequences.ExistsArgument(Dialect, sequence);
        var found = await ScalarAsync(command, "sequence " + sequence, ct).ConfigureAwait(false);
        return found is not null and not DBNull && Convert.ToInt64(found, CultureInfo.InvariantCulture) != 0;
    }

    /// <summary>The sequence's next value. The sequence exists (its DDL is <see cref="SqlSequences.CreateSql" />'s, or <see cref="EnsureSequenceAsync" />).</summary>
    public async ValueTask<long> NextValueAsync(string sequence, CancellationToken ct = default)
    {
        SqlSequences.Validate(sequence);
        var command = BatchCommand("seq:" + sequence, d => SqlSequences.NextSql(d, sequence));
        if (SqlSequences.IsEmulated(Dialect))
            command.Parameters[0].Value = sequence;
        var value = await ScalarAsync(command, "sequence " + sequence, ct).ConfigureAwait(false);
        if (value is null || value is DBNull)
            throw new InvalidOperationException($"Sequence '{sequence}' is not there: create it with the statements of SqlSequences.CreateSql.");
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     The sequence's next <paramref name="count" /> values in one round trip, in order: a batch of rows numbered before their insert.
    ///     Consecutive on SQLite; on PostgreSQL and SQL Server as the sequence hands them out (its increment, no other session in between
    ///     on SQL Server, which reserves the range; on PostgreSQL another session may take a value in the middle: the values are what
    ///     <c>nextval</c> returned, in order).
    /// </summary>
    public async ValueTask<long[]> NextValuesAsync(string sequence, int count, CancellationToken ct = default)
    {
        SqlSequences.Validate(sequence);
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "a range has at least one value");
        var command = BatchCommand("seqn:" + sequence, d => SqlSequences.RangeSql(d, sequence));
        var values = new long[count];
        switch (Dialect)
        {
            case SqlDialect.Sqlite:
            case SqlDialect.MySql:
            {
                command.Parameters[0].Value = sequence;
                command.Parameters[1].Value = count;
                var last = await ScalarAsync(command, "sequence " + sequence, ct).ConfigureAwait(false);
                if (last is null || last is DBNull)
                    throw new InvalidOperationException($"Sequence '{sequence}' is not there: create it with the statements of SqlSequences.CreateSql.");
                var end = Convert.ToInt64(last, CultureInfo.InvariantCulture);
                for (var i = 0; i < count; i++)
                    values[i] = end - count + 1 + i;
                return values;
            }

            case SqlDialect.PostgreSql:
            case SqlDialect.Oracle:
            {
                command.Parameters[0].Value = count;
                using var reader = await ReadAsync(command, "sequence " + sequence, ct).ConfigureAwait(false);
                var i = 0;
                while (await reader.ReadAsync(ct).ConfigureAwait(false) && i < count)
                    values[i++] = reader.GetInt64(0);
                if (i != count)
                    throw new InvalidOperationException($"Sequence '{sequence}' returned {i} values, not {count}.");
                return values;
            }

            default:
            {
                command.Parameters[0].Value = count;
                using var reader = await ReadAsync(command, "sequence " + sequence, ct).ConfigureAwait(false);
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    throw new InvalidOperationException($"Sequence '{sequence}' returned no range.");
                var first = reader.GetInt64(0);
                var increment = reader.GetInt64(1);
                for (var i = 0; i < count; i++)
                    values[i] = first + i * increment;
                return values;
            }
        }
    }
}
