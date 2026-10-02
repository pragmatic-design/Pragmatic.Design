using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Options;
using Pragmatic.Identity.Keycloak;
using Xunit;

namespace Pragmatic.Identity.Keycloak.Tests;

/// <summary>
///     Verifies the Keycloak-specific role mapping (auth, layer 2): the nested <c>realm_access</c> JSON object
///     becomes standard <see cref="ClaimTypes.Role"/> claims, and the optional allow-list / prefix filter
///     constrains which realm roles are mapped (#ID-K1).
/// </summary>
public class KeycloakRoleClaimsTransformerTests
{
    private static KeycloakRoleClaimsTransformer Transformer(KeycloakOptions? options = null) =>
        new(Options.Create(new IdentityOptions()), Options.Create(options ?? new KeycloakOptions()));

    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "keycloak"));

    private static string[] Roles(ClaimsPrincipal principal) =>
        principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();

    [Fact]
    public async Task RealmAccessRoles_BecomeRoleClaims()
    {
        var principal = PrincipalWith(new Claim("realm_access", """{"roles":["admin","billing"]}"""));

        var result = await Transformer().TransformAsync(principal);

        Roles(result).Should().BeEquivalentTo("admin", "billing");
    }

    [Fact]
    public async Task NoRealmAccessClaim_AddsNoRoles()
    {
        var principal = PrincipalWith(new Claim("sub", "user-1"));

        var result = await Transformer().TransformAsync(principal);

        Roles(result).Should().BeEmpty();
    }

    [Fact]
    public async Task MalformedRealmAccess_AddsNoRoles_DoesNotThrow()
    {
        var principal = PrincipalWith(new Claim("realm_access", "not-json"));

        var result = await Transformer().TransformAsync(principal);

        Roles(result).Should().BeEmpty();
    }

    [Fact]
    public async Task Transform_IsIdempotent()
    {
        var principal = PrincipalWith(new Claim("realm_access", """{"roles":["admin"]}"""));
        var transformer = Transformer();

        await transformer.TransformAsync(principal);
        await transformer.TransformAsync(principal);

        Roles(principal).Should().ContainSingle().Which.Should().Be("admin");
    }

    [Fact]
    public async Task AllowedRoles_FiltersOutNonListedRoles()
    {
        var principal = PrincipalWith(new Claim("realm_access", """{"roles":["admin","billing","stray"]}"""));
        var options = new KeycloakOptions { AllowedRoles = ["admin", "billing"] };

        var result = await Transformer(options).TransformAsync(principal);

        Roles(result).Should().BeEquivalentTo("admin", "billing");
    }

    [Fact]
    public async Task RolePrefix_MapsOnlyPrefixedRoles()
    {
        var principal = PrincipalWith(new Claim("realm_access", """{"roles":["myapp:admin","other:admin"]}"""));
        var options = new KeycloakOptions { RolePrefix = "myapp:" };

        var result = await Transformer(options).TransformAsync(principal);

        Roles(result).Should().BeEquivalentTo("myapp:admin");
    }

    [Fact]
    public async Task AllowedRolesAndPrefix_MustBothMatch()
    {
        var principal = PrincipalWith(
            new Claim("realm_access", """{"roles":["myapp:admin","myapp:stray","other:admin"]}"""));
        var options = new KeycloakOptions { RolePrefix = "myapp:", AllowedRoles = ["myapp:admin"] };

        var result = await Transformer(options).TransformAsync(principal);

        Roles(result).Should().BeEquivalentTo("myapp:admin");
    }

    [Fact]
    public async Task NullFilters_PassEveryRole()
    {
        var principal = PrincipalWith(new Claim("realm_access", """{"roles":["a","b","c"]}"""));

        var result = await Transformer(new KeycloakOptions()).TransformAsync(principal);

        Roles(result).Should().BeEquivalentTo("a", "b", "c");
    }
}
