using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Text;

namespace Pragmatic.Persistence.Sql;

/// <summary>Another writer changed or removed the row since it was read: the update or delete matched nothing.</summary>
public sealed class SqlConcurrencyException : InvalidOperationException
{
    public SqlConcurrencyException(string table, object? key, object? version)
        : base($"The row of \"{table}\" with key {key} was not at version {version}: another writer changed or deleted it.")
    {
        Table = table;
        Key = key;
        Version = version;
    }

    /// <summary>A batch of statements wrote fewer rows than it sent: which rows were stale is not known.</summary>
    public SqlConcurrencyException(string table, int written, int sent)
        : base($"The batch on \"{table}\" wrote {written} of {sent} rows: another writer changed or deleted the others.")
    {
        Table = table;
    }

    public string Table { get; }

    public object? Key { get; }

    public object? Version { get; }
}
