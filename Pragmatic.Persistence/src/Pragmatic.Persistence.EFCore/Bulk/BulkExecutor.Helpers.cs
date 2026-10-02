using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Pragmatic.Persistence.EFCore.Bulk;

public static partial class BulkExecutor
{
    /// <summary>
    ///     Builds entity metadata (table name, column mappings, provider) from the EF Core model.
    ///     Called once per entity/provider combination and cached.
    /// </summary>
    private static CachedEntityMetadata BuildMetadata<T>(
        DbContext db,
        BulkEntityDescriptor<T> descriptor) where T : class
    {
        var entityType = db.Model.FindEntityType(typeof(T))
                         ?? throw new InvalidOperationException(
                             $"Entity type '{typeof(T).FullName}' is not part of the EF Core model for DbContext '{db.GetType().FullName}'.");

        var tableName = entityType.GetTableName()
                        ?? throw new InvalidOperationException(
                            $"Entity type '{typeof(T).FullName}' does not have a table mapping.");

        var schema = entityType.GetSchema();
        var provider = DetectProvider(db);

        var storeObject = StoreObjectIdentifier.Table(tableName, schema);

        var columns = new (string PropertyName, string ColumnName, BulkColumnRole Role)[descriptor.Columns.Length];

        for (var i = 0; i < descriptor.Columns.Length; i++)
        {
            var (propertyName, role) = descriptor.Columns[i];
            var property = entityType.FindProperty(propertyName);

            var columnName = property?.GetColumnName(storeObject)
                             ?? property?.GetColumnName()
                             ?? propertyName; // Fallback to property name if no mapping found

            columns[i] = (propertyName, columnName, role);
        }

        return new CachedEntityMetadata
        {
            TableName = tableName,
            Schema = schema,
            Columns = columns,
            Provider = provider,
        };
    }

    /// <summary>
    ///     Detects the database provider from the DbContext, caching by DbContext type.
    /// </summary>
    private static ProviderType DetectProvider(DbContext db)
    {
        return ProviderCache.GetOrAdd((db.GetType(), db.Database.ProviderName), _ =>
        {
            var providerName = db.Database.ProviderName ?? string.Empty;

            if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
                return ProviderType.SqlServer;
            if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
                return ProviderType.PostgreSql;
            if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
                return ProviderType.Sqlite;

            throw new InvalidOperationException(
                $"Unsupported database provider '{providerName}'. " +
                "BulkExecutor supports SQL Server, PostgreSQL, and SQLite.");
        });
    }

    /// <summary>
    ///     Quotes a database identifier according to the provider's convention.
    /// </summary>
    private static string QuoteIdentifier(string name, ProviderType provider)
    {
        // Double the delimiter so a name containing it stays well-formed. Names come from EF
        // metadata (compile-time), so this is latent robustness, not an injection fix — but it matches
        // HierarchyCteSql/TimelineCteSql and avoids broken SQL if a [Table]/[Column] ever contains one.
        return provider switch
        {
            ProviderType.SqlServer => $"[{name.Replace("]", "]]")}]",
            // PostgreSQL / SQLite: quote to handle reserved words and special characters.
            ProviderType.PostgreSql or ProviderType.Sqlite => $"\"{name.Replace("\"", "\"\"")}\"",
            _ => name
        };
    }

    /// <summary>
    ///     Formats the fully qualified table name including schema if present.
    /// </summary>
    private static string FormatTableName(CachedEntityMetadata metadata)
    {
        var quotedTable = QuoteIdentifier(metadata.TableName, metadata.Provider);

        if (metadata.Schema is not null && metadata.Provider != ProviderType.Sqlite)
        {
            var quotedSchema = QuoteIdentifier(metadata.Schema, metadata.Provider);
            return $"{quotedSchema}.{quotedTable}";
        }

        return quotedTable;
    }

    /// <summary>
    ///     Creates and adds a DbParameter to the command.
    /// </summary>
    private static void AddParameter(DbCommand cmd, string name, object? value)
    {
        var param = cmd.CreateParameter();
        param.ParameterName = name;
        param.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(param);
    }
}
