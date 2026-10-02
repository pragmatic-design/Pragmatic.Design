using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Static CLR→SQL type mapping for 3 database providers.
///     Used by SchemaMetadataTransform to generate provider-specific column types.
/// </summary>
internal static class SqlTypeMapper
{
    public static string MapToSqlType(
        string clrTypeName,
        EfCoreProvider provider,
        int? maxLength = null,
        int? precision = null,
        int? scale = null,
        bool isEnum = false)
    {
        if (isEnum)
            return provider == EfCoreProvider.SqlServer ? "int" : "integer";

        // Primitive collections (List<T>, T[], IReadOnlyList<T>, … of a scalar/enum element).
        // A collection reaching the schema is necessarily a primitive collection — collections of
        // entities are navigations, excluded from Properties. Provider mapping mirrors EF Core:
        //   PostgreSQL → Npgsql native array of the element type (text[], integer[], uuid[], …)
        //   SqlServer/SQLite → EF stores the collection as a JSON string column.
        if (TryGetCollectionElement(clrTypeName, out var elementClr))
        {
            if (provider == EfCoreProvider.SqlServer)
                return "nvarchar(max)";
            if (provider != EfCoreProvider.PostgreSql)
                return "TEXT";

            var elementNorm = NormalizeClrType(elementClr);
            // Known scalar → its PG type; anything else is an enum (EF stores enums as int) → integer.
            var elementPg = IsKnownScalar(elementNorm) ? LookupBaseType(elementNorm, provider) : "integer";
            return elementPg + "[]";
        }

        var normalized = NormalizeClrType(clrTypeName);

        // String with MaxLength
        if (normalized == "string" && maxLength.HasValue)
            return provider switch
            {
                EfCoreProvider.PostgreSql => $"varchar({maxLength.Value})",
                EfCoreProvider.SqlServer => $"nvarchar({maxLength.Value})",
                _ => "TEXT"
            };

        // Decimal with precision/scale
        if (normalized == "decimal" && precision.HasValue)
        {
            var s = scale ?? 2;
            return provider switch
            {
                EfCoreProvider.PostgreSql => $"numeric({precision.Value},{s})",
                EfCoreProvider.SqlServer => $"decimal({precision.Value},{s})",
                _ => "REAL"
            };
        }

        return LookupBaseType(normalized, provider);
    }

    private static string LookupBaseType(string clrType, EfCoreProvider provider) => (clrType, provider) switch
    {
        // String (no max length)
        ("string", EfCoreProvider.PostgreSql) => "text",
        ("string", EfCoreProvider.SqlServer) => "nvarchar(max)",
        ("string", _) => "TEXT",

        // Integer types
        ("int", EfCoreProvider.PostgreSql) => "integer",
        ("int", EfCoreProvider.SqlServer) => "int",
        ("int", _) => "INTEGER",

        ("long", EfCoreProvider.PostgreSql) => "bigint",
        ("long", EfCoreProvider.SqlServer) => "bigint",
        ("long", _) => "INTEGER",

        ("short", EfCoreProvider.PostgreSql) => "smallint",
        ("short", EfCoreProvider.SqlServer) => "smallint",
        ("short", _) => "INTEGER",

        ("byte", EfCoreProvider.PostgreSql) => "smallint",
        ("byte", EfCoreProvider.SqlServer) => "tinyint",
        ("byte", _) => "INTEGER",

        // Boolean
        ("bool", EfCoreProvider.PostgreSql) => "boolean",
        ("bool", EfCoreProvider.SqlServer) => "bit",
        ("bool", _) => "INTEGER",

        // Floating point
        ("decimal", EfCoreProvider.PostgreSql) => "numeric(18,2)",
        ("decimal", EfCoreProvider.SqlServer) => "decimal(18,2)",
        ("decimal", _) => "REAL",

        ("double", EfCoreProvider.PostgreSql) => "double precision",
        ("double", EfCoreProvider.SqlServer) => "float",
        ("double", _) => "REAL",

        ("float", EfCoreProvider.PostgreSql) => "real",
        ("float", EfCoreProvider.SqlServer) => "real",
        ("float", _) => "REAL",

        // Guid
        ("guid", EfCoreProvider.PostgreSql) => "uuid",
        ("guid", EfCoreProvider.SqlServer) => "uniqueidentifier",
        ("guid", _) => "TEXT",

        // Date/Time
        ("datetime", EfCoreProvider.PostgreSql) => "timestamp",
        ("datetime", EfCoreProvider.SqlServer) => "datetime2",
        ("datetime", _) => "TEXT",

        ("datetimeoffset", EfCoreProvider.PostgreSql) => "timestamptz",
        ("datetimeoffset", EfCoreProvider.SqlServer) => "datetimeoffset",
        ("datetimeoffset", _) => "TEXT",

        ("dateonly", EfCoreProvider.PostgreSql) => "date",
        ("dateonly", EfCoreProvider.SqlServer) => "date",
        ("dateonly", _) => "TEXT",

        ("timeonly", EfCoreProvider.PostgreSql) => "time",
        ("timeonly", EfCoreProvider.SqlServer) => "time",
        ("timeonly", _) => "TEXT",

        ("timespan", EfCoreProvider.PostgreSql) => "interval",
        ("timespan", EfCoreProvider.SqlServer) => "time",
        ("timespan", _) => "TEXT",

        // Binary
        ("byte[]", EfCoreProvider.PostgreSql) => "bytea",
        ("byte[]", EfCoreProvider.SqlServer) => "varbinary(max)",
        ("byte[]", _) => "BLOB",

        // (Primitive collections are handled earlier in MapToSqlType via TryGetCollectionElement.)

        // Fallback
        _ => provider switch
        {
            EfCoreProvider.SqlServer => "nvarchar(max)",
            EfCoreProvider.PostgreSql => "text",
            _ => "TEXT"
        }
    };

    /// <summary>
    ///     Normalizes CLR type names: strips nullable, System. prefix, global::, and maps aliases.
    /// </summary>
    internal static string NormalizeClrType(string typeName)
    {
        var t = typeName.Trim();

        // Strip nullable wrapper
        if (t.EndsWith("?", StringComparison.Ordinal))
            t = t.TrimEnd('?');
        if (t.StartsWith("System.Nullable<", StringComparison.Ordinal))
        {
            t = t.Substring("System.Nullable<".Length);
            if (t.EndsWith(">", StringComparison.Ordinal))
                t = t.TrimEnd('>');
        }

        // Strip global:: and System. prefixes
        if (t.StartsWith("global::", StringComparison.Ordinal))
            t = t.Substring(8);
        if (t.StartsWith("System.", StringComparison.Ordinal))
            t = t.Substring(7);

        // A ProtectedValue is the bytes its converter writes, and the schema has to say so or the
        // migration and the EF model describe different columns — the entity maps it through
        // ProtectedValueConverter to byte[].
        if (ProtectedValueType.Matches(t))
            return "byte[]";

        // Normalize common aliases
        return t.ToLowerInvariant() switch
        {
            "int32" => "int",
            "int64" => "long",
            "int16" => "short",
            "single" => "float",
            "boolean" => "bool",
            "string" => "string",
            "decimal" => "decimal",
            "double" => "double",
            "byte" => "byte",
            "guid" => "guid",
            "datetime" => "datetime",
            "datetimeoffset" => "datetimeoffset",
            "dateonly" => "dateonly",
            "timeonly" => "timeonly",
            "timespan" => "timespan",
            "byte[]" => "byte[]",
            var other => other
        };
    }

    private static bool IsKnownScalar(string normalized) => normalized is
        "string" or "int" or "long" or "short" or "byte" or "bool" or "decimal" or "double"
        or "float" or "guid" or "datetime" or "datetimeoffset" or "dateonly" or "timeonly" or "timespan";

    /// <summary>
    ///     Detects a collection-of-primitives type (<c>List&lt;T&gt;</c>, <c>T[]</c>,
    ///     <c>IReadOnlyList&lt;T&gt;</c>, …) and extracts the element CLR type name. <c>byte[]</c> is a
    ///     binary scalar, not a collection. Element type is left raw for <see cref="NormalizeClrType"/>.
    /// </summary>
    private static bool TryGetCollectionElement(string typeName, out string elementClr)
    {
        elementClr = "";
        var t = typeName.Trim();
        if (t.EndsWith("?", StringComparison.Ordinal))
            t = t.TrimEnd('?');

        // Array form: T[] — but byte[] is a binary blob, not a collection.
        if (t.EndsWith("[]", StringComparison.Ordinal))
        {
            var elem = t.Substring(0, t.Length - 2);
            if (NormalizeClrType(elem) == "byte")
                return false;
            elementClr = elem;
            return true;
        }

        // Generic collection form: namespace-qualified List<T>, IEnumerable<T>, HashSet<T>, …
        var lt = t.IndexOf('<');
        var gt = t.LastIndexOf('>');
        if (lt < 0 || gt <= lt)
            return false;

        var openName = t.Substring(0, lt);
        var lastDot = openName.LastIndexOf('.');
        var simple = lastDot >= 0 ? openName.Substring(lastDot + 1) : openName;
        var isCollection = simple is "List" or "IList" or "ICollection" or "IReadOnlyList"
            or "IReadOnlyCollection" or "IEnumerable" or "HashSet" or "Collection";
        if (!isCollection)
            return false;

        elementClr = t.Substring(lt + 1, gt - lt - 1);
        return true;
    }
}
