using System.Collections.Immutable;
using System.Text.Json;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Cli.Snapshot;

/// <summary>
///     Serializes a <see cref="SchemaVersion" /> to the deterministic JSON written into
///     the committed <c>schema/</c> snapshot folder. The output is canonical (tables,
///     indexes and foreign keys sorted by name) and byte-stable across operating systems,
///     so a CI git-diff gate reliably detects when the snapshot drifted from the entities.
/// </summary>
public static class SchemaSnapshotSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>Serializes the schema to canonical, newline-normalized JSON (trailing newline included).</summary>
    public static string Serialize(SchemaVersion schema)
    {
        var json = JsonSerializer.Serialize(Canonicalize(schema), JsonOptions);
        // Normalize line endings so the committed file is byte-identical across OSes.
        return json.ReplaceLineEndings("\n") + "\n";
    }

    /// <summary>
    ///     Produces a stable ordering — tables, indexes and foreign keys sorted by name —
    ///     so the serialized JSON is diff-friendly and order-independent across builds.
    ///     Column order is preserved: it is meaningful and already deterministic.
    /// </summary>
    private static SchemaVersion Canonicalize(SchemaVersion schema)
    {
        var tables = schema.Tables
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => t with
            {
                Indexes = [.. t.Indexes.OrderBy(i => i.Name, StringComparer.Ordinal)],
                ForeignKeys = [.. t.ForeignKeys.OrderBy(f => f.Name, StringComparer.Ordinal)]
            })
            .ToImmutableArray();

        return schema with { Tables = tables };
    }
}
