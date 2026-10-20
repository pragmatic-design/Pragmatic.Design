using System.Text.Json;
using Pragmatic.Persistence.Query.Adapters;
using Pragmatic.Testing.Assertions;
using PersistenceQuery = Pragmatic.Persistence.Query.Query;

namespace Pragmatic.Persistence.Tests.AdapterTests;

/// <summary>
///     A JSON integer in a filter is read as an integer by both runtime adapters (#317). It was read as a
///     <see cref="double" />: <c>TryGetInt64(out var l) ? l : GetDouble()</c> has the type <c>double</c>, so an enum
///     column filtered by number dropped the clause (<see cref="Enum.ToObject(Type, object)" /> refuses a double) and a
///     <see cref="long" /> past 2^53 matched its neighbour.
/// </summary>
public class AJsonIntegerIsReadAsAnIntegerTests
{
    private enum Kind
    {
        City = 1,
        Town = 2,
    }

    private sealed class Item
    {
        public string Name { get; set; } = "";
        public Kind Kind { get; set; }
        public long Serial { get; set; }
    }

    // 2^53 + 1 and 2^53: a double cannot tell them apart.
    private static readonly Item[] Items =
    [
        new() { Name = "Rome", Kind = Kind.City, Serial = 9007199254740993 },
        new() { Name = "Turin", Kind = Kind.Town, Serial = 9007199254740992 },
    ];

    [Theory]
    [InlineData("""{"filter":["kind","=",2]}""", "Turin")]
    [InlineData("""{"filter":["serial","=",9007199254740993]}""", "Rome")]
    public void DevExpress_reads_a_json_integer_as_an_integer(string json, string expected)
    {
        var options = JsonSerializer.Deserialize<DevExpressLoadOptions>(json, JsonSerializerOptions.Web)!;

        var rows = PersistenceQuery.For<Item>().FromDevExpress(options).Build(Items.AsQueryable()).ToList();

        rows.Select(row => row.Name).Should().Equal(expected);
    }

    [Theory]
    [InlineData("""{"filters":{"kind":{"value":2,"matchMode":"equals"}}}""", "Turin")]
    [InlineData("""{"filters":{"serial":{"value":9007199254740993,"matchMode":"equals"}}}""", "Rome")]
    public void PrimeNG_reads_a_json_integer_as_an_integer(string json, string expected)
    {
        var request = JsonSerializer.Deserialize<PrimeNGLazyLoadEvent>(json, JsonSerializerOptions.Web)!;

        var rows = PersistenceQuery.For<Item>().FromPrimeNG(request).Build(Items.AsQueryable()).ToList();

        rows.Select(row => row.Name).Should().Equal(expected);
    }
}
