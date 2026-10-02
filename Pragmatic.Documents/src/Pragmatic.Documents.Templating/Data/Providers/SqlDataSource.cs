using System.Data.Common;

namespace Pragmatic.Documents.Templating.Data.Providers;

/// <summary>
/// Data source that executes a raw SQL query and returns rows as dictionaries.
/// Each row is a <c>Dictionary&lt;string, object?&gt;</c> keyed by column name.
/// Returns <c>List&lt;Dictionary&lt;string, object?&gt;&gt;</c> for multi-row queries,
/// or a single <c>Dictionary&lt;string, object?&gt;</c> for single-row queries.
/// </summary>
public sealed class SqlDataSource : IDataSourceProvider
{
    private readonly Func<DbConnection> _connectionFactory;
    private readonly string _sql;
    private readonly Action<DbCommand>? _configureCommand;
    private readonly bool _singleRow;

    public string Name { get; }
    public Type ValueType => _singleRow
        ? typeof(Dictionary<string, object?>)
        : typeof(List<Dictionary<string, object?>>);

    /// <param name="name">Source name.</param>
    /// <param name="connectionFactory">Factory that creates a new DbConnection (caller manages connection string).</param>
    /// <param name="sql">
    ///     Static SQL query to execute. MUST be a compile-time constant or otherwise developer-controlled string —
    ///     never pass user-supplied input here. Use <paramref name="configureCommand"/> to bind any runtime values
    ///     as parameterized query parameters.
    /// </param>
    /// <param name="configureCommand">
    ///     Add parameterized values to the command (e.g. cmd.Parameters.AddWithValue("@id", id)).
    ///     Use this for all dynamic values to prevent SQL injection.
    /// </param>
    /// <param name="singleRow">If true, returns a single row as a dictionary. Otherwise returns a list of rows.</param>
    public SqlDataSource(
        string name,
        Func<DbConnection> connectionFactory,
        string sql,
        Action<DbCommand>? configureCommand = null,
        bool singleRow = false)
    {
        Name = name;
        _connectionFactory = connectionFactory;
        _sql = sql;
        _configureCommand = configureCommand;
        _singleRow = singleRow;
    }

    public async ValueTask<object?> ResolveAsync(CancellationToken ct = default)
    {
        await using var connection = _connectionFactory();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = _sql;
        _configureCommand?.Invoke(command);

        await using var reader = await command.ExecuteReaderAsync(ct);

        if (_singleRow)
        {
            if (await reader.ReadAsync(ct))
                return ReadRow(reader);
            return null;
        }

        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(ct))
            rows.Add(ReadRow(reader));

        return rows;
    }

    private static Dictionary<string, object?> ReadRow(DbDataReader reader)
    {
        var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
            row[reader.GetName(i)] = value;
        }
        return row;
    }
}
