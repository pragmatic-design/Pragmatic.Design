using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Diff;

/// <summary>
///     Pairs the tables of two schemas, tolerating that one side often omits the schema qualifier.
///     <para>
///     The compile-time schema usually leaves <see cref="TableSchema.SchemaName" /> null while
///     introspection always fills it in, so matching cannot be a plain equality on
///     (schema, name). Nor can it be an equality comparer that treats null as "matches anything":
///     that relation is not transitive (<c>(null,"Orders")</c> equals both
///     <c>("public","Orders")</c> and <c>("archive","Orders")</c>, which do not equal each other),
///     and a Dictionary built on a non-transitive comparer resolves such a lookup to whichever
///     entry happens to sit first in the bucket. In a multi-schema database that is a silent diff
///     against an arbitrary table.
///     </para>
///     <para>
///     Matching is therefore explicit and order-independent: an exact (schema, name) match first,
///     then — only when the requested schema is unknown and exactly ONE table carries that bare
///     name — a fallback by name. An ambiguous bare name matches nothing, which surfaces as a
///     visible create/drop pair rather than a silent wrong pairing.
///     </para>
/// </summary>
internal sealed class TableMatcher
{
    private readonly Dictionary<(string Schema, string Name), TableSchema> _qualified = new(QualifiedComparer.Instance);
    private readonly Dictionary<string, List<TableSchema>> _byName = new(StringComparer.OrdinalIgnoreCase);

    public TableMatcher(IEnumerable<TableSchema> tables)
    {
        foreach (var table in tables)
        {
            // A null schema is indexed under the empty qualifier so exact lookups still work for
            // providers that have no schemas at all (SQLite).
            _qualified[(table.SchemaName ?? string.Empty, table.Name)] = table;

            if (!_byName.TryGetValue(table.Name, out var sameName))
                _byName[table.Name] = sameName = [];
            sameName.Add(table);
        }
    }

    /// <summary>All tables, in the order they were supplied.</summary>
    public IEnumerable<TableSchema> All => _qualified.Values;

    /// <summary>
    ///     Finds the table matching <paramref name="table" />, or null when there is none — or when
    ///     the bare name is ambiguous across schemas and the requested schema is unknown.
    /// </summary>
    public TableSchema? Find(TableSchema table)
    {
        if (table.SchemaName is not null &&
            _qualified.TryGetValue((table.SchemaName, table.Name), out var exact))
            return exact;

        if (!_byName.TryGetValue(table.Name, out var candidates))
            return null;

        // Unqualified request: unambiguous only when a single table carries that name.
        if (table.SchemaName is null)
            return candidates.Count == 1 ? candidates[0] : null;

        // Qualified request with no exact hit: accept a candidate that itself declares no schema.
        var unqualified = candidates.Where(c => c.SchemaName is null).ToList();
        return unqualified.Count == 1 ? unqualified[0] : null;
    }

    private sealed class QualifiedComparer : IEqualityComparer<(string Schema, string Name)>
    {
        public static readonly QualifiedComparer Instance = new();

        public bool Equals((string Schema, string Name) x, (string Schema, string Name) y) =>
            string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Schema, string Name) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Schema),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
    }
}
