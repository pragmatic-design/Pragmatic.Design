using Pragmatic.Testing.Assertions;

namespace Pragmatic.Configuration.Tests.Unit;

public class EnvironmentProfileTests
{
    [Fact]
    public void From_Development_HasCorrectChain()
    {
        var profile = EnvironmentProfile.From("Development");

        profile.Name.Should().Be("Development");
        profile.IsDevelopment.Should().BeTrue();
        profile.IsStaging.Should().BeFalse();
        profile.IsProduction.Should().BeFalse();
        profile.Tag.Should().BeNull();
        profile.ResolutionChain.Should().BeEquivalentTo(["base", "development"]);
    }

    [Fact]
    public void From_Production_HasBaseOnlyChain()
    {
        var profile = EnvironmentProfile.From("Production");

        profile.IsProduction.Should().BeTrue();
        profile.ResolutionChain.Should().BeEquivalentTo(["base"]);
    }

    [Fact]
    public void From_Staging_HasCorrectChain()
    {
        var profile = EnvironmentProfile.From("Staging");

        profile.IsStaging.Should().BeTrue();
        profile.ResolutionChain.Should().BeEquivalentTo(["base", "staging"]);
    }

    [Fact]
    public void From_WithTag_HasThreeLevelChain()
    {
        var profile = EnvironmentProfile.From("Staging", "eu-west");

        profile.Tag.Should().Be("eu-west");
        profile.ResolutionChain.Should().BeEquivalentTo(["base", "staging", "staging-eu-west"]);
    }

    [Fact]
    public void From_ProductionWithTag_HasTwoLevelChain()
    {
        var profile = EnvironmentProfile.From("Production", "canary");

        profile.Tag.Should().Be("canary");
        // Production skips env name, but tag still applies
        profile.ResolutionChain.Should().BeEquivalentTo(["base", "production-canary"]);
    }

    [Fact]
    public void From_CaseInsensitive_IsDevelopment()
    {
        var profile = EnvironmentProfile.From("DEVELOPMENT");
        profile.IsDevelopment.Should().BeTrue();
    }

    [Fact]
    public void From_CustomEnvironment_AddedToChain()
    {
        var profile = EnvironmentProfile.From("QA");

        profile.IsDevelopment.Should().BeFalse();
        profile.IsStaging.Should().BeFalse();
        profile.IsProduction.Should().BeFalse();
        profile.ResolutionChain.Should().BeEquivalentTo(["base", "qa"]);
    }
}
