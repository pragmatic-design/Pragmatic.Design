using System.Text.Json;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>
///     The census as a committed file: the samples project sets <c>PragmaticCensusFile</c>, so its build writes <c>queries.census.json</c>
///     (one line per translated query: file, member, kind, the chain as written, the SQL per dialect) and compares the committed one
///     with the fresh one (PRAG3015 when stale: see <see cref="DiagnosticsTests" />, whose fixture commits a stale census).
/// </summary>
public class CensusTests
{
    private static readonly string Census = TestPaths.Sample("Pragmatic.Persistence.Sql.Samples", "queries.census.json");

    [Fact]
    public void The_samples_census_lists_every_translated_query_with_its_sql_per_dialect()
    {
        Assert.True(File.Exists(Census), Census);
        using var document = JsonDocument.Parse(File.ReadAllText(Census));
        var entries = document.RootElement.EnumerateArray().ToList();
        Assert.True(entries.Count > 100, entries.Count.ToString());
        foreach (var entry in entries)
        {
            foreach (var key in new[] { "file", "member", "kind", "chain", "sqlite", "postgresql", "sqlserver" })
                Assert.Equal(JsonValueKind.String, entry.GetProperty(key).ValueKind);
            Assert.EndsWith(".cs", entry.GetProperty("file").GetString(), StringComparison.Ordinal);
        }

        // Sorted by file, member and chain: a stable order, so a diff shows the query that changed and nothing else.
        var keys = entries.Select(e => (e.GetProperty("file").GetString(), e.GetProperty("member").GetString(), e.GetProperty("chain").GetString())).ToList();
        Assert.Equal(keys.OrderBy(k => k.Item1, StringComparer.Ordinal).ThenBy(k => k.Item2, StringComparer.Ordinal).ThenBy(k => k.Item3, StringComparer.Ordinal), keys);
        Assert.Contains(entries, e => e.GetProperty("kind").GetString() == "[Query]");
        Assert.Contains(entries, e => e.GetProperty("sqlserver").GetString()!.Contains("[Orders]", StringComparison.Ordinal) && e.GetProperty("postgresql").GetString()!.Contains("\"Orders\"", StringComparison.Ordinal));
    }
}
