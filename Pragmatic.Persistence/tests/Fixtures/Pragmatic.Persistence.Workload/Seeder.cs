using System.Data.Common;
using System.Globalization;
using System.Text;
using Pragmatic.Persistence.Sql;

namespace Pragmatic.Persistence.Workload;

/// <summary>
///     Drops, creates and fills the workload schema on an open connection: the one seeding routine the tests,
///     the benchmark harness and the AOT smoke share, so every database holds the same rows.
/// </summary>
public static class Seeder
{
    private const int Batch = 200;

    public static async Task SeedAsync(DbConnection connection, SqlDialect dialect, Seed seed)
    {
        foreach (var statement in Schema.Drop(dialect))
            await Execute(connection, statement);
        foreach (var statement in Schema.Create(dialect))
            await Execute(connection, statement);

        using var transaction = connection.BeginTransaction();
        await Insert(connection, transaction, dialect, "Customers", ["Id", "Name", "Email", "Country", "CreatedAt"], seed.Customers, c => [c.Id, c.Name, c.Email, c.Country, c.CreatedAt]);
        await Insert(connection, transaction, dialect, "Orders", ["Id", "CustomerId", "Number", "Status", "Total", "CreatedAt", "IsDeleted"], seed.Orders, o => [o.Id, o.CustomerId, o.Number, (int)o.Status, o.Total, o.CreatedAt, o.IsDeleted]);
        await Insert(connection, transaction, dialect, "OrderLines", ["Id", "OrderId", "Sku", "Quantity", "UnitPrice"], seed.Lines, l => [l.Id, l.OrderId, l.Sku, l.Quantity, l.UnitPrice]);
        transaction.Commit();
        // MySQL's index statistics after a bulk load say every key has every row until ANALYZE runs: a correlated subquery then scans the table
        // (measured: 217 s for EF's nested projection against 0.2 s). PostgreSQL and SQL Server sample on their own.
        if (dialect == SqlDialect.MySql)
            await Execute(connection, "ANALYZE TABLE \"Customers\", \"Orders\", \"OrderLines\"");
    }

    private static async Task Execute(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Multi-row INSERT in batches: the fastest portable way without each provider's bulk API.</summary>
    private static async Task Insert<T>(DbConnection connection, DbTransaction transaction, SqlDialect dialect, string table, string[] columns, IReadOnlyList<T> rows, Func<T, object[]> values)
    {
        for (var start = 0; start < rows.Count; start += Batch)
        {
            var count = Math.Min(Batch, rows.Count - start);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            var sql = new StringBuilder();
            sql.Append("INSERT INTO ").Append(Schema.Quote(dialect, table)).Append(" (");
            for (var c = 0; c < columns.Length; c++)
                sql.Append(c > 0 ? ", " : "").Append(Schema.Quote(dialect, columns[c]));
            sql.Append(") VALUES ");
            var p = 0;
            for (var r = 0; r < count; r++)
            {
                sql.Append(r > 0 ? ", (" : "(");
                var row = values(rows[start + r]);
                for (var c = 0; c < columns.Length; c++)
                {
                    var name = "@p" + p.ToString(CultureInfo.InvariantCulture);
                    sql.Append(c > 0 ? ", " : "").Append(name);
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = name;
                    parameter.Value = SqlValue.ToDatabase(dialect, row[c]);
                    command.Parameters.Add(parameter);
                    p++;
                }

                sql.Append(')');
            }

            command.CommandText = sql.ToString();
            if (dialect == SqlDialect.Oracle)
                SqlOracle.Adapt(command); // :pN in the text, pN as the parameters' names
            await command.ExecuteNonQueryAsync();
        }
    }
}
