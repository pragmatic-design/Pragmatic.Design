using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Pragmatic.Documents.Templating.Data.Providers;

/// <summary>
/// Extension methods on <see cref="DataSourceCatalog"/> for built-in providers.
/// </summary>
public static class DataSourceCatalogExtensions
{
    extension(DataSourceCatalog catalog)
    {
        /// <summary>Add a JSON file data source (single object, AOT-safe).</summary>
        public DataSourceCatalog AddJsonFile<T>(string name, string filePath, JsonTypeInfo<T> typeInfo)
            where T : class
            => catalog.AddProvider(new JsonFileDataSource<T>(name, filePath, typeInfo));

        /// <summary>Add a JSON file data source (single object, convenience).</summary>
        [RequiresUnreferencedCode("Use the JsonTypeInfo overload for AOT.")]
        [RequiresDynamicCode("Use the JsonTypeInfo overload for AOT.")]
        public DataSourceCatalog AddJsonFile<T>(string name, string filePath, JsonSerializerOptions? options = null)
            where T : class
            => catalog.AddProvider(new JsonFileDataSource<T>(name, filePath, options));

        /// <summary>Add a JSON file data source (list, AOT-safe).</summary>
        public DataSourceCatalog AddJsonFileList<T>(string name, string filePath, JsonTypeInfo<List<T>> typeInfo)
            where T : class
            => catalog.AddProvider(new JsonFileListDataSource<T>(name, filePath, typeInfo));

        /// <summary>Add a JSON file data source (list, convenience).</summary>
        [RequiresUnreferencedCode("Use the JsonTypeInfo overload for AOT.")]
        [RequiresDynamicCode("Use the JsonTypeInfo overload for AOT.")]
        public DataSourceCatalog AddJsonFileList<T>(string name, string filePath, JsonSerializerOptions? options = null)
            where T : class
            => catalog.AddProvider(new JsonFileListDataSource<T>(name, filePath, options));

        /// <summary>Add a raw SQL query data source (returns list of row dictionaries).</summary>
        public DataSourceCatalog AddSql(string name,
            Func<DbConnection> connectionFactory, string sql,
            Action<DbCommand>? configureCommand = null)
            => catalog.AddProvider(new SqlDataSource(name, connectionFactory, sql, configureCommand));

        /// <summary>Add a raw SQL query data source (returns single row dictionary).</summary>
        public DataSourceCatalog AddSqlSingle(string name,
            Func<DbConnection> connectionFactory, string sql,
            Action<DbCommand>? configureCommand = null)
            => catalog.AddProvider(new SqlDataSource(name, connectionFactory, sql, configureCommand, singleRow: true));
    }
}
