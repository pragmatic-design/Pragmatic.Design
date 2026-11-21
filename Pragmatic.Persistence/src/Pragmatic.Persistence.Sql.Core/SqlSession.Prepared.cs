using System.Data;
using System.Data.Common;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     The commands the session keeps and prepares, and the one case where a kept command cannot run as prepared (#920): on SQL Server a
///     prepared statement keeps the declarations it was prepared with, and SqlClient sends no more of a value than the size its parameter had
///     then. A string parameter is typed at 4000 (<see cref="Planning.SqlValue" />) and widened to its value when longer
///     (<see cref="Planning.SqlParameterFit" />), but the widening reaches a command only before it is prepared: the second call of a
///     statement in a session wrote, or compared, the first 4000 characters of a longer text, with no error.
/// </summary>
public sealed partial class SqlSession
{
    /// <summary>The size a string parameter is prepared with, which a longer value does not fit on SQL Server.</summary>
    private const int PreparedStringSize = 4000;

    private HashSet<DbCommand>? _prepared;

    /// <summary>Prepares <paramref name="command" />, one the session keeps, and remembers it was.</summary>
    internal void Prepare(DbCommand command)
    {
        command.Prepare();
        (_prepared ??= new HashSet<DbCommand>(ReferenceEqualityComparer.Instance)).Add(command);
    }

    /// <inheritdoc cref="Prepare(DbCommand)" />
    internal async Task PrepareAsync(DbCommand command, CancellationToken ct)
    {
        await command.PrepareAsync(ct).ConfigureAwait(false);
        (_prepared ??= new HashSet<DbCommand>(ReferenceEqualityComparer.Instance)).Add(command);
    }

    /// <summary>
    ///     The command to run for <paramref name="command" />, its values bound: itself, but on SQL Server for a prepared command a string value
    ///     is longer than its prepared size, where it is an unprepared copy that takes its place wherever the session keeps it. Its later calls
    ///     run unprepared, which sends each value whole; the statements whose values fit stay prepared.
    /// </summary>
    internal DbCommand Runnable(DbCommand command)
    {
        if (Dialect != SqlDialect.SqlServer || _prepared is null || !_prepared.Contains(command) || !Overflows(command.Parameters)
            || command is not ICloneable cloneable)
            return command;

        var copy = (DbCommand)cloneable.Clone();
        copy.Transaction = command.Transaction;
        Replace(command, copy);
        _prepared.Remove(command);
        command.Dispose();
        return copy;
    }

    private static bool Overflows(DbParameterCollection parameters)
    {
        foreach (DbParameter parameter in parameters)
        {
            if (parameter.DbType == DbType.String && parameter.Value is string text && text.Length > PreparedStringSize)
                return true;
        }

        return false;
    }

    /// <summary><paramref name="kept" /> replaced by <paramref name="copy" /> wherever the session keeps it: a plan's, a filtered variant's, a batch's.</summary>
    private void Replace(DbCommand kept, DbCommand copy)
    {
        for (var i = 0; i < _commands.Length; i++)
        {
            if (ReferenceEquals(_commands[i], kept))
                _commands[i] = copy;
        }

        if (_filtered is not null)
        {
            foreach (var key in _filtered.Where(entry => ReferenceEquals(entry.Value, kept)).Select(entry => entry.Key).ToList())
                _filtered[key] = copy;
        }

        if (_batches is not null)
        {
            foreach (var key in _batches.Where(entry => ReferenceEquals(entry.Value, kept)).Select(entry => entry.Key).ToList())
                _batches[key] = copy;
        }
    }
}
