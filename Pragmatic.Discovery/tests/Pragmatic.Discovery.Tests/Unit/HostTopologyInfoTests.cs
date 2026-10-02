using Pragmatic.Testing.Assertions;
using Pragmatic.Discovery.Models;

namespace Pragmatic.Discovery.Tests.Unit;

public class HostTopologyInfoTests
{
    [Fact]
    public void Parse_WithValidJson_ReturnsTopology()
    {
        var json = """
            {
                "host": "MyHost",
                "includes": [
                    { "module": "BillingModule", "database": "FinancialDb", "provider": "SqlServer" }
                ],
                "boundaries": [
                    { "name": "Booking", "readAccess": ["Property", "RoomType"] }
                ]
            }
            """;

        var result = HostTopologyInfo.Parse(json);

        result.Should().NotBeNull();
        result!.HostName.Should().Be("MyHost");
        result.Modules.Should().HaveCount(1);
        result.Modules[0].ModuleName.Should().Be("BillingModule");
        result.Modules[0].DatabaseName.Should().Be("FinancialDb");
        result.Modules[0].Provider.Should().Be("SqlServer");
        result.Boundaries.Should().HaveCount(1);
        result.Boundaries[0].BoundaryName.Should().Be("Booking");
        result.Boundaries[0].EntityTypes.Should().BeEquivalentTo("Property", "RoomType");
    }

    [Fact]
    public void Parse_WithMultipleModules_ReturnsAllModules()
    {
        var json = """
            {
                "host": "MultiHost",
                "includes": [
                    { "module": "BillingModule", "database": "FinancialDb", "provider": "SqlServer" },
                    { "module": "CatalogModule", "database": "CatalogDb", "provider": "Postgres" }
                ]
            }
            """;

        var result = HostTopologyInfo.Parse(json);

        result.Should().NotBeNull();
        result!.Modules.Should().HaveCount(2);
        result.Modules[0].ModuleName.Should().Be("BillingModule");
        result.Modules[1].ModuleName.Should().Be("CatalogModule");
    }

    [Fact]
    public void Parse_WithEmptyIncludes_ReturnsEmptyModules()
    {
        var json = """
            {
                "host": "EmptyHost",
                "includes": []
            }
            """;

        var result = HostTopologyInfo.Parse(json);

        result.Should().NotBeNull();
        result!.HostName.Should().Be("EmptyHost");
        result.Modules.Should().BeEmpty();
    }

    [Fact]
    public void Parse_WithEmptyBoundaries_ReturnsEmptyBoundaries()
    {
        var json = """
            {
                "host": "NoBoundaries",
                "includes": [],
                "boundaries": []
            }
            """;

        var result = HostTopologyInfo.Parse(json);

        result.Should().NotBeNull();
        result!.Boundaries.Should().BeEmpty();
    }

    [Fact]
    public void Parse_WithNoBoundariesProperty_ReturnsEmptyBoundaries()
    {
        var json = """
            {
                "host": "NoBoundariesProp",
                "includes": []
            }
            """;

        var result = HostTopologyInfo.Parse(json);

        result.Should().NotBeNull();
        result!.Boundaries.Should().BeEmpty();
    }

    [Fact]
    public void Parse_WithNullInput_ReturnsNull()
    {
        var result = HostTopologyInfo.Parse(null!);

        result.Should().BeNull();
    }

    [Fact]
    public void Parse_WithEmptyString_ReturnsNull()
    {
        var result = HostTopologyInfo.Parse(string.Empty);

        result.Should().BeNull();
    }

    [Fact]
    public void Parse_WithWhitespace_ReturnsNull()
    {
        var result = HostTopologyInfo.Parse("   ");

        result.Should().BeNull();
    }

    [Fact]
    public void Parse_WithInvalidJson_ReturnsNull()
    {
        var result = HostTopologyInfo.Parse("not valid json {{{");

        result.Should().BeNull();
    }

    [Theory]
    // DISC-M3 regression: valid JSON with the WRONG SHAPE must return null, not throw
    // InvalidOperationException from EnumerateArray()/GetString().
    [InlineData("""{ "host": "H", "includes": { } }""")]        // includes is an object, not an array
    [InlineData("""{ "host": 123 }""")]                          // host is a number, not a string
    [InlineData("""{ "host": "H", "boundaries": "nope" }""")]    // boundaries is a string, not an array
    [InlineData("""{ "host": "H", "boundaries": [ { "name": "B", "readAccess": [ 1, 2 ] } ] }""")]
    public void Parse_WithValidJsonButWrongShape_ReturnsNull(string json)
    {
        var result = HostTopologyInfo.Parse(json);

        result.Should().BeNull();
    }

    [Fact]
    public void Parse_WithNoHostProperty_ReturnsUnknownHostName()
    {
        var json = """
            {
                "includes": []
            }
            """;

        var result = HostTopologyInfo.Parse(json);

        result.Should().NotBeNull();
        result!.HostName.Should().Be("Unknown");
    }

    [Fact]
    public void Parse_WithModuleMissingOptionalFields_ReturnsNullForOptionals()
    {
        var json = """
            {
                "host": "MinimalHost",
                "includes": [
                    { "module": "SimpleModule" }
                ]
            }
            """;

        var result = HostTopologyInfo.Parse(json);

        result.Should().NotBeNull();
        result!.Modules.Should().HaveCount(1);
        result.Modules[0].ModuleName.Should().Be("SimpleModule");
        result.Modules[0].DatabaseName.Should().BeNull();
        result.Modules[0].Provider.Should().BeNull();
    }

    [Fact]
    public void Parse_WithBoundaryEmptyReadAccess_ReturnsEmptyEntityTypes()
    {
        var json = """
            {
                "host": "Host",
                "boundaries": [
                    { "name": "Isolated", "readAccess": [] }
                ]
            }
            """;

        var result = HostTopologyInfo.Parse(json);

        result.Should().NotBeNull();
        result!.Boundaries.Should().HaveCount(1);
        result.Boundaries[0].BoundaryName.Should().Be("Isolated");
        result.Boundaries[0].EntityTypes.Should().BeEmpty();
    }

    [Fact]
    public void Parse_RegisteredAt_IsSetToRecentUtcTime()
    {
        var before = DateTimeOffset.UtcNow;

        var json = """{ "host": "TimedHost", "includes": [] }""";
        var result = HostTopologyInfo.Parse(json);

        var after = DateTimeOffset.UtcNow;

        result.Should().NotBeNull();
        result!.RegisteredAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }
}
