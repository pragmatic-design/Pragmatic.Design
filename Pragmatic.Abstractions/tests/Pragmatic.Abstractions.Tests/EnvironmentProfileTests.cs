using Pragmatic.Testing.Assertions;
using Pragmatic.Configuration;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

public sealed class EnvironmentProfileTests
{
    [Theory]
    [InlineData("Development", true, false, false, false)]
    [InlineData("staging", false, true, false, false)]     // case-insensitive
    [InlineData("PRODUCTION", false, false, true, false)]
    [InlineData("Testing", false, false, false, true)]
    [InlineData("QA", false, false, false, false)]         // custom name matches nothing
    public void WellKnownFlags_MatchCaseInsensitive(
        string name, bool dev, bool staging, bool production, bool testing)
    {
        var profile = new EnvironmentProfile { Name = name };

        profile.IsDevelopment.Should().Be(dev);
        profile.IsStaging.Should().Be(staging);
        profile.IsProduction.Should().Be(production);
        profile.IsTesting.Should().Be(testing);
    }

    [Fact]
    public void IsEnvironment_CustomName_MatchesCaseInsensitive()
    {
        var profile = new EnvironmentProfile { Name = "UAT" };

        profile.IsEnvironment("uat").Should().BeTrue();
        profile.IsEnvironment("qa").Should().BeFalse();
    }

    [Fact]
    public void From_NonProduction_ChainsBaseThenEnvironment()
        => EnvironmentProfile.From("Staging").ResolutionChain.Should().Equal("base", "staging");

    [Fact]
    public void From_Production_OmitsEnvironmentOverlay()
        => EnvironmentProfile.From("Production").ResolutionChain.Should().Equal("base");

    [Fact]
    public void From_WithTag_AppendsTagOverlay()
        => EnvironmentProfile.From("Staging", "eu-west").ResolutionChain
            .Should().Equal("base", "staging", "staging-eu-west");

    [Fact]
    public void From_ProductionWithTag_KeepsTagButNoEnvironmentOverlay()
        => EnvironmentProfile.From("Production", "canary").ResolutionChain
            .Should().Equal("base", "production-canary");
}
