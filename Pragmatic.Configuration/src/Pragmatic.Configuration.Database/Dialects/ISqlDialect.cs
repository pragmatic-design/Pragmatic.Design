namespace Pragmatic.Configuration.Database.Dialects;

/// <summary>
///     Abstraction for SQL dialect differences between database providers.
///     Each dialect provides provider-specific SQL for configuration storage.
/// </summary>
internal interface ISqlDialect
{
    /// <summary>SELECT value for a single key (with optional tenant + environment).</summary>
    string GetValue { get; }

    /// <summary>INSERT or UPDATE (upsert) a configuration value.</summary>
    string SetValue { get; }

    /// <summary>DELETE a configuration value by key.</summary>
    string DeleteValue { get; }

    /// <summary>SELECT all key-value pairs matching a prefix (section query).</summary>
    string GetSection { get; }

    /// <summary>SELECT encrypted secret value by key.</summary>
    string GetSecret { get; }

    /// <summary>INSERT or UPDATE an encrypted secret.</summary>
    string SetSecret { get; }

    /// <summary>DELETE a secret by key (with optional tenant).</summary>
    string DeleteSecret { get; }

    /// <summary>SELECT every secret (key, encrypted value, tenant) — used by the key-rotation re-encrypt pass.</summary>
    string GetAllSecrets { get; }

    /// <summary>SELECT recently changed rows since a given timestamp.</summary>
    string GetChangesSince { get; }

    /// <summary>DDL to create all required tables if they don't exist.</summary>
    string CreateSchema { get; }

    /// <summary>
    ///     Escapes LIKE metacharacters (%, _, and provider-specific wildcards) in a prefix string
    ///     so that the value can be safely used as the left operand of a LIKE @escapedPrefix + '%' expression.
    /// </summary>
    string EscapeLikePattern(string prefix);

    /// <summary>
    ///     Converts a watcher cursor into the value bound to <c>@since</c> in <see cref="GetChangesSince"/>,
    ///     in the exact precision/format that the dialect stores in <c>updated_at</c>. This keeps the
    ///     <c>updated_at &gt; @since</c> comparison precise so a change written shortly after the cursor
    ///     is not skipped (SQLite stores text timestamps, so a second-precision store would lose a
    ///     same-second change against a sub-second cursor).
    /// </summary>
    object FormatChangeCursor(DateTimeOffset cursor);
}
