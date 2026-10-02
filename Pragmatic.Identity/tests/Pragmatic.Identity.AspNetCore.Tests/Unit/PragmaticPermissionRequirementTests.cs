using Pragmatic.Testing.Assertions;
using Pragmatic.Identity.Authorization;
using Pragmatic.Endpoints.Authorization;

namespace Pragmatic.Identity.AspNetCore.Tests.Unit;

public sealed class PragmaticPermissionRequirementTests
{
    [Fact]
    public void Constructor_WithNullPermissions_Throws()
    {
        var act = () => new PragmaticPermissionRequirement(null!, PermissionMode.Any);

        act.Should().Throw<ArgumentNullException>().WithParameterName("permissions");
    }

    [Fact]
    public void Constructor_WithEmptyPermissions_Throws()
    {
        var act = () => new PragmaticPermissionRequirement([], PermissionMode.Any);

        act.Should().Throw<ArgumentException>().WithParameterName("permissions");
    }

    [Fact]
    public void Constructor_StoresPermissions()
    {
        var requirement = new PragmaticPermissionRequirement(
            ["orders.read", "orders.write"], PermissionMode.All);

        requirement.Permissions.Should().BeEquivalentTo("orders.read", "orders.write");
    }

    [Fact]
    public void Constructor_PreservesRequestedMode()
    {
        var requirement = new PragmaticPermissionRequirement(
            ["orders.read", "orders.write"], PermissionMode.Any);

        requirement.Mode.Should().Be(PermissionMode.Any);
    }

    [Fact]
    public void Constructor_DefaultsToAllMode()
    {
        var requirement = new PragmaticPermissionRequirement(["orders.read"]);

        requirement.Mode.Should().Be(PermissionMode.All);
    }
}
