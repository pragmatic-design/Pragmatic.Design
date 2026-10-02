using Pragmatic.Testing.Assertions;

namespace Pragmatic.Testing.Tests;

/// <summary>
///     The dev-identity headers <see cref="PragmaticTestIdentity" /> writes.
/// </summary>
/// <remarks>
///     Roles are expressible because against a host that calls <c>UseAuthorization(authz =&gt; …)</c>
///     the permission claim is ignored — the resolver derives permissions from the role map — so a caller
///     carrying the exact permission still gets 403, and without roles the only way out is writing the
///     header by hand.
/// </remarks>
public class PragmaticTestIdentityTests
{
    private static readonly Uri Endpoint = new("https://localhost/api/orders");

    [Fact]
    public void AsUser_WithRoles_WritesTheRolesHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);

        request.AsUser("u1", roles: ["direzione-lavori", "capocantiere"]);

        request.Headers.GetValues(PragmaticTestIdentity.RolesHeader)
            .Should().Contain("direzione-lavori,capocantiere");
    }

    [Fact]
    public void AsUser_WithGroups_WritesTheGroupsHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);

        request.AsUser("u1", groups: ["studio-milano"]);

        request.Headers.GetValues(PragmaticTestIdentity.GroupsHeader).Should().Contain("studio-milano");
    }

    [Fact]
    public void AsUser_WithoutRoles_WritesNoRolesHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);

        request.AsUser("u1", permissions: ["orders.read"]);

        request.Headers.Contains(PragmaticTestIdentity.RolesHeader)
            .Should().BeFalse("an absent role list must not turn into an empty claim");
    }

    /// <summary>
    ///     The permission header stays written-when-empty on a single request: that is what makes an
    ///     underprivileged caller underprivileged rather than an inheritor of the client's default grant.
    /// </summary>
    [Fact]
    public void AsUser_OnRequestWithoutPermissions_StillWritesAnEmptyPermissionHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);

        request.AsUser("u1", roles: ["revisore"]);

        request.Headers.GetValues(PragmaticTestIdentity.PermissionsHeader).Should().Contain("");
    }
}
