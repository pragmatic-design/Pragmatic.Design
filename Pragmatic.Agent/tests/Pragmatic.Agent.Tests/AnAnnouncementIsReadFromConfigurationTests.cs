using Microsoft.Extensions.Configuration;
using Pragmatic.Agent.Client;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Agent.Tests;

/// <summary>The route a host announces comes from <c>Pragmatic:Agent:Announce</c>.</summary>
public sealed class AnAnnouncementIsReadFromConfigurationTests
{
    [Fact]
    public void ARouteWithPathAndAddress_IsAnnounced()
    {
        var announce = PragmaticBuilderAgentExtensions.AnnouncementFrom(Configuration(new()
        {
            ["Pragmatic:Agent:Announce:RouteId"] = "warehouse",
            ["Pragmatic:Agent:Announce:Path"] = "/warehouse/{**catch-all}",
            ["Pragmatic:Agent:Announce:PathRemovePrefix"] = "/warehouse",
            ["Pragmatic:Agent:Announce:RequireAuth"] = "true",
            ["Pragmatic:Agent:Announce:Address"] = "http://127.0.0.1:5222",
        }))!;

        (announce.RouteId, announce.Path, announce.PathRemovePrefix, announce.RequireAuth, announce.Address)
            .Should().Be(("warehouse", "/warehouse/{**catch-all}", "/warehouse", true, "http://127.0.0.1:5222"));
    }

    /// <summary>The control: a host that configures no route announces none.</summary>
    [Fact]
    public void NoRouteId_AnnouncesNothing()
        => PragmaticBuilderAgentExtensions.AnnouncementFrom(Configuration(new())).Should().BeNull();

    [Fact]
    public void ARouteWithoutAnAddress_IsRefusedAtStartup()
    {
        var read = () => PragmaticBuilderAgentExtensions.AnnouncementFrom(Configuration(new()
        {
            ["Pragmatic:Agent:Announce:RouteId"] = "warehouse",
            ["Pragmatic:Agent:Announce:Path"] = "/warehouse/{**catch-all}",
        }));

        read.Should().Throw<InvalidOperationException>().WithMessage("*Announce:Address*");
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
