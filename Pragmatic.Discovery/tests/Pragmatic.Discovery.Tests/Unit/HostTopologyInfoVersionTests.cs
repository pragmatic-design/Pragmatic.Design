using Pragmatic.Testing.Assertions;
using Pragmatic.Discovery.Models;
using Xunit;

namespace Pragmatic.Discovery.Tests.Unit;

/// <summary>
/// The SG→consumer topology contract is versioned. <see cref="HostTopologyInfo.IsCompatibleVersion"/>
/// gates a metadata schema version by MAJOR, and <see cref="HostTopologyInfo.Parse"/> reads the
/// <c>configKey</c> / <c>dbContext</c> fields the generator emits.
/// </summary>
public class HostTopologyInfoVersionTests
{
    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("1.9.9", true)]   // minor/patch tolerated
    [InlineData("1", true)]       // major only
    [InlineData("2.0.0", false)]  // major mismatch → incompatible
    [InlineData("0.9.0", false)]
    [InlineData(null, true)]      // no version → best-effort
    [InlineData("", true)]
    [InlineData("abc", true)]     // unparseable major → best-effort (emitter owns the format)
    public void IsCompatibleVersion_GatesByMajor(string? version, bool expected)
    {
        HostTopologyInfo.IsCompatibleVersion(version).Should().Be(expected);
    }

    [Fact]
    public void SchemaMajor_MatchesEmittedContract()
    {
        // The consumer's expected major must track the version the SG emits (MetadataSchemaVersions.HostTopology).
        HostTopologyInfo.SchemaMajor.Should().Be(1);
    }

    [Fact]
    public void Parse_ReadsConfigKeyAndDbContext()
    {
        var json = """
            {
                "host": "H",
                "includes": [
                    {
                        "module": "BillingModule",
                        "database": "FinancialDb",
                        "provider": "SqlServer",
                        "configKey": "ConnectionStrings:Financial",
                        "dbContext": "FinancialDbContext"
                    }
                ]
            }
            """;

        var result = HostTopologyInfo.Parse(json);

        result.Should().NotBeNull();
        result!.Modules[0].ConfigKey.Should().Be("ConnectionStrings:Financial");
        result.Modules[0].DbContext.Should().Be("FinancialDbContext");
    }

    [Fact]
    public void Parse_WithoutConfigKeyOrDbContext_LeavesThemNull()
    {
        var json = """{ "host": "H", "includes": [ { "module": "M", "database": "D" } ] }""";

        var result = HostTopologyInfo.Parse(json);

        result.Should().NotBeNull();
        result!.Modules[0].ConfigKey.Should().BeNull();
        result.Modules[0].DbContext.Should().BeNull();
    }
}
