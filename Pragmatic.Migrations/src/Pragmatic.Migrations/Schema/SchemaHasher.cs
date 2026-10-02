using System.Security.Cryptography;
using System.Text;
using Pragmatic.Migrations.Diff;

namespace Pragmatic.Migrations.Schema;

/// <summary>
///     Computes the canonical identity of a schema: a hash that is equal for two schemas the
///     <see cref="ISchemaDiffEngine" /> would report as identical, whatever produced them.
///     <para>
///     This is why the hash lives here and not in the producers. When the compile-time generator
///     hashed its own text and each introspector hashed its own, the same schema got a different
///     hash on each side — the generator writes <c>integer</c> where PostgreSQL reports <c>int4</c>
///     — so the two could never be compared. Both sides now go through this one function, which
///     applies exactly the normalisation the diff applies before comparing.
///     </para>
/// </summary>
public static class SchemaHasher
{
    /// <summary>
    ///     Canonical hash of a schema: 12 lowercase hex characters (48 bits of SHA-256).
    /// </summary>
    /// <remarks>
    ///     The digest covers precisely what the diff engine compares — no more, no less. Anything
    ///     omitted here would make two schemas that the diff considers different share a hash;
    ///     anything extra would make two identical schemas differ. <c>SchemaHasherTests</c> pins
    ///     both directions, one case per kind of change.
    /// </remarks>
    public static string Compute(SchemaVersion schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var sb = new StringBuilder();

        // Order is normalised everywhere: introspection returns tables in catalog order, the
        // generator in declaration order, and neither is a difference the diff would report.
        foreach (var table in schema.Tables.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            // The schema qualifier is deliberately NOT hashed: the generator usually leaves it null
            // while introspection always fills it in, and the diff treats a null as "wherever the
            // table actually lives".
            sb.Append("T:").Append(Normalize(table.Name)).Append('\n');

            // Column ORDER is not compared by the diff (it diffs by name), so sort here too.
            foreach (var column in table.Columns.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append("  C:").Append(Normalize(column.Name))
                  .Append('|').Append(SchemaDiffEngine.NormalizeSqlType(column.SqlType))
                  .Append('|').Append(column.IsNullable ? 'n' : 'N')
                  .Append('|').Append(SchemaDiffEngine.NormalizeDefault(column.DefaultValue) ?? "-")
                  .Append('\n');
            }

            // The primary key is an ordered list — its column order defines the index order.
            var pk = table.Columns.Where(c => c.IsPrimaryKey).Select(c => Normalize(c.Name));
            sb.Append("  PK:").Append(string.Join(",", pk)).Append('\n');

            // Indexes and FKs are matched STRUCTURALLY by the diff, never by name — so the name is
            // not part of the identity, but every attribute that is compared must be.
            var indexes = table.Indexes
                .Select(i => $"{string.Join(",", i.Columns.Select(Normalize))}|{(i.IsUnique ? "u" : "-")}|{Normalize(i.Filter) ?? "-"}")
                .OrderBy(x => x, StringComparer.Ordinal);
            foreach (var index in indexes)
                sb.Append("  IX:").Append(index).Append('\n');

            var foreignKeys = table.ForeignKeys
                .Select(f => $"{Normalize(f.Column)}->{Normalize(f.ReferencedTable)}.{Normalize(f.ReferencedColumn)}|{f.OnDelete}")
                .OrderBy(x => x, StringComparer.Ordinal);
            foreach (var fk in foreignKeys)
                sb.Append("  FK:").Append(fk).Append('\n');
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(digest)[..12].ToLowerInvariant();
    }

    // Identifiers are compared case-insensitively by the diff, so the hash must fold case too.
    private static string? Normalize(string? value) => value?.ToUpperInvariant();
}
