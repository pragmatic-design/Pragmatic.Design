namespace Pragmatic.Persistence.Sql;

/// <summary>Named savepoints in the session's transaction, in SQL: SqlClient and MySqlConnector support them but report <c>SupportsSavepoints</c> false.</summary>
public sealed partial class SqlSession
{
    /// <summary>Sets a savepoint in the open transaction (<c>SAVE TRANSACTION</c> on SQL Server, <c>SAVEPOINT</c> elsewhere).</summary>
    public Task SavepointAsync(string name, CancellationToken ct = default)
        => RunSavepointAsync(Dialect == SqlDialect.SqlServer ? "SAVE TRANSACTION " : "SAVEPOINT ", name, ct);

    /// <summary>Undoes what the transaction did since the savepoint, which stays set.</summary>
    public Task RollbackToSavepointAsync(string name, CancellationToken ct = default)
        => RunSavepointAsync(Dialect == SqlDialect.SqlServer ? "ROLLBACK TRANSACTION " : "ROLLBACK TO SAVEPOINT ", name, ct);

    private async Task RunSavepointAsync(string verb, string name, CancellationToken ct)
    {
        if (Transaction is null)
            throw new InvalidOperationException("No transaction is open on this session: a savepoint lives in one.");
        // The name goes into the text: an identifier only, never a value.
        if (name.Length == 0 || !(char.IsAsciiLetter(name[0]) || name[0] == '_') || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw new ArgumentException($"\"{name}\" is not a savepoint name: letters, digits and '_', not starting with a digit.", nameof(name));
        using var command = FreshCommand(verb + name, []);
        await RunAsync(command, "savepoint " + name, ct).ConfigureAwait(false);
    }
}
