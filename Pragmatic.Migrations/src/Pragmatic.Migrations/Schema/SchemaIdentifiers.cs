using System.Collections.Immutable;

namespace Pragmatic.Migrations.Schema;

/// <summary>
///     The names of a schema's indexes, foreign keys and checks, fitted to the most characters a database allows in a name.
/// </summary>
/// <remarks>
///     <para>
///         MySQL refuses a name over 64 characters, so a schema that declares one could not be migrated at all; PostgreSQL truncates one over 63
///         itself. A generated schema has such a name already: EF Core's index on <c>__OutboxMessages</c> is 70 characters.
///     </para>
///     <para>
///         The rule is EF Core's (<c>Uniquifier.Truncate</c>: the first <c>limit − 1</c> characters, then <c>~</c>), with the limits its providers
///         declare, so a database EF Core migrated and one Pragmatic migrated carry the same names. Fitted in the desired schema, before the
///         diff: the generated DDL, the catalogue the next run reads and that run's diff then agree, which matters for the checks, compared by
///         name (indexes and foreign keys are compared by what they cover).
///     </para>
/// </remarks>
public static class SchemaIdentifiers
{
    /// <summary>
    ///     <paramref name="schema" /> with every index, foreign key and check name over <paramref name="maxLength" /> characters fitted to it; the
    ///     same instance when there is no limit or no name reaches it.
    /// </summary>
    public static SchemaVersion FitTo(SchemaVersion schema, int? maxLength)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (maxLength is not { } limit || !schema.Tables.Any(t => Overlong(t, limit)))
            return schema;

        // A new instance, not `with`: the copy would carry the hash already computed for the unfitted schema.
        return new SchemaVersion([.. schema.Tables.Select(t => Overlong(t, limit) ? Fit(t, limit) : t)], schema.DatabaseName, schema.ProviderName, schema.ConfigKey);
    }

    /// <summary><paramref name="name" /> fitted to <paramref name="maxLength" /> characters as EF Core fits it.</summary>
    public static string Fit(string name, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Length <= maxLength ? name : string.Concat(name.AsSpan(0, maxLength - 1), "~");
    }

    private static bool Overlong(TableSchema table, int limit)
        => table.Indexes.Any(i => i.Name.Length > limit) || table.ForeignKeys.Any(f => f.Name.Length > limit)
            || table.CheckConstraints.Any(c => c.Name.Length > limit);

    private static TableSchema Fit(TableSchema table, int limit) => table with
    {
        Indexes = [.. table.Indexes.Select(i => i with { Name = Fit(i.Name, limit) })],
        ForeignKeys = [.. table.ForeignKeys.Select(f => f with { Name = Fit(f.Name, limit) })],
        CheckConstraints = [.. table.CheckConstraints.Select(c => c with { Name = Fit(c.Name, limit) })],
    };
}
