using System.Text.Json;
using Xunit;

namespace Pragmatic.Persistence.Documents.Core.Tests;

/// <summary>
///     The order a merge across Cosmos DB's partitions follows (<see cref="CosmosOrder" />), without a database: by type first (undefined, null,
///     booleans, numbers, strings), then by value; descending reversed; keys in order. The emulator has one partition key range, so this is
///     where the merge's comparison is checked.
/// </summary>
public class CosmosOrderTests
{
    private static JsonElement Doc(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static List<string> Sorted(IEnumerable<JsonElement> documents, params (string Field, bool Descending)[] keys)
        => documents.OrderBy(d => d, new CosmosOrder(keys.Select(k => ((IReadOnlyList<string>)[k.Field], k.Descending)).ToList())).Select(d => d.GetProperty("id").GetString()!).ToList();

    [Fact]
    public void Values_are_ordered_by_type_then_by_value()
    {
        var documents = new[]
        {
            Doc("""{"id":"s-b","v":"b"}"""), Doc("""{"id":"n-10","v":10}"""), Doc("""{"id":"t","v":true}"""), Doc("""{"id":"null","v":null}"""),
            Doc("""{"id":"missing"}"""), Doc("""{"id":"s-B","v":"B"}"""), Doc("""{"id":"n-2.5","v":2.5}"""), Doc("""{"id":"f","v":false}"""),
        };
        Assert.Equal(["missing", "null", "f", "t", "n-2.5", "n-10", "s-B", "s-b"], Sorted(documents, ("v", false)));
        Assert.Equal(["s-b", "s-B", "n-10", "n-2.5", "t", "f", "null", "missing"], Sorted(documents, ("v", true)));
    }

    [Fact]
    public void A_second_key_breaks_the_first_ones_ties()
    {
        var documents = new[] { Doc("""{"id":"a2","k":1,"t":"2"}"""), Doc("""{"id":"b","k":0,"t":"9"}"""), Doc("""{"id":"a1","k":1,"t":"1"}""") };
        Assert.Equal(["b", "a2", "a1"], Sorted(documents, ("k", false), ("t", true)));
    }

    [Fact]
    public void Instants_as_stored_order_as_time()
    {
        var earlier = DocJson.Instant(new DateTimeOffset(2026, 1, 2, 9, 0, 0, TimeSpan.FromHours(2)));
        var later = DocJson.Instant(new DateTimeOffset(2026, 1, 2, 8, 30, 0, TimeSpan.Zero));
        var documents = new[] { Doc($$"""{"id":"later","at":"{{later}}"}"""), Doc($$"""{"id":"earlier","at":"{{earlier}}"}""") };
        Assert.Equal(["earlier", "later"], Sorted(documents, ("at", false)));
    }
}
