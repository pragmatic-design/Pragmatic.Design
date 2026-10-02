using Npgsql;

namespace TimeOff.IntegrationTests.Infrastructure;

/// <summary>
///     Searches every text column of a database for a value, as written and percent-escaped.
/// </summary>
/// <remarks>
///     <para>
///         The check that personal data is gone from <em>everywhere</em>, not from the columns someone
///         thought of: an author stamp, an access scope, an audit entry and an identity key all hold who
///         someone is without being classified as personal data, and an erasure that misses one looks
///         complete. Encrypted columns (<c>bytea</c>) are left out: what they hold cannot be read without
///         the key.
///     </para>
///     <para>
///         Escaped too, because that is how an identity key writes an email — <c>local|ada%40example.com</c>
///         — and a search for the email as typed walked past it.
///     </para>
/// </remarks>
internal static class DatabaseText
{
    /// <returns><c>table.column</c> for every column holding <paramref name="text" />, in either form.</returns>
    public static async Task<IReadOnlyList<string>> FindAsync(string connectionString, string text)
    {
        var found = new List<string>();
        foreach (var form in new[] { text, Uri.EscapeDataString(text) }.Distinct(StringComparer.Ordinal))
            found.AddRange(await FindAsWrittenAsync(connectionString, form));

        return [.. found.Distinct(StringComparer.Ordinal)];
    }

    private static async Task<IReadOnlyList<string>> FindAsWrittenAsync(string connectionString, string text)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var columns = new List<(string Schema, string Table, string Column)>();
        await using (var list = new NpgsqlCommand(
                         """
                         SELECT table_schema, table_name, column_name
                         FROM information_schema.columns
                         WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
                           AND data_type IN ('text', 'character varying', 'character', 'json', 'jsonb', 'ARRAY')
                         """, connection))
        await using (var reader = await list.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                columns.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        var found = new List<string>();
        foreach (var (schema, table, column) in columns)
        {
            await using var probe = new NpgsqlCommand(
                $"""SELECT EXISTS (SELECT 1 FROM "{schema}"."{table}" WHERE "{column}"::text ILIKE '%' || @text || '%')""",
                connection);
            probe.Parameters.AddWithValue("text", text);

            if (await probe.ExecuteScalarAsync() is true)
                found.Add($"{table}.{column}");
        }

        return found;
    }
}
