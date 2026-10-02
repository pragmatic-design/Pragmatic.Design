using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Options;
using Pragmatic.Identity.Oidc;
using Xunit;

namespace Pragmatic.Identity.Oidc.Tests;

/// <summary>
///     Verifies the generic OIDC role-claim mapping (auth, layer 1): the external IdP's role claim becomes
///     standard <see cref="ClaimTypes.Role"/> claims, whether emitted as repeated claims or a JSON array.
/// </summary>
public class OidcRoleClaimsTransformerTests
{
    private static OidcRoleClaimsTransformer Transformer(string roleClaim = "roles") =>
        new(Options.Create(new OidcOptions { RoleClaim = roleClaim }), Options.Create(new IdentityOptions()));

    private static OidcRoleClaimsTransformer Transformer(OidcOptions options) =>
        new(Options.Create(options), Options.Create(new IdentityOptions()));

    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "oidc"));

    private static string[] Roles(ClaimsPrincipal principal) =>
        principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();

    [Fact]
    public async Task RepeatedRoleClaims_BecomeRoleClaims()
    {
        var principal = PrincipalWith(new Claim("roles", "admin"), new Claim("roles", "billing"));

        var result = await Transformer().TransformAsync(principal);

        Roles(result).Should().BeEquivalentTo("admin", "billing");
    }

    [Fact]
    public async Task JsonArrayRoleClaim_IsExpanded()
    {
        var principal = PrincipalWith(new Claim("roles", "[\"admin\",\"user\"]"));

        var result = await Transformer().TransformAsync(principal);

        Roles(result).Should().BeEquivalentTo("admin", "user");
    }

    [Fact]
    public async Task Transform_IsIdempotent()
    {
        var principal = PrincipalWith(new Claim("roles", "admin"));
        var transformer = Transformer();

        await transformer.TransformAsync(principal);
        await transformer.TransformAsync(principal);

        Roles(principal).Should().ContainSingle().Which.Should().Be("admin");
    }

    [Fact]
    public async Task CustomRoleClaimName_IsHonoured()
    {
        var principal = PrincipalWith(new Claim("groups", "ops"));

        var result = await Transformer("groups").TransformAsync(principal);

        Roles(result).Should().BeEquivalentTo("ops");
    }

    [Fact]
    public async Task AllowedRoles_FiltersOutNonListedRoles()
    {
        var principal = PrincipalWith(new Claim("roles", "admin"), new Claim("roles", "stray"));

        var result = await Transformer(new OidcOptions { AllowedRoles = ["admin"] })
            .TransformAsync(principal);

        Roles(result).Should().BeEquivalentTo("admin");
    }

    [Fact]
    public async Task RolePrefix_MapsOnlyPrefixedRoles()
    {
        var principal = PrincipalWith(new Claim("roles", "[\"myapp:admin\",\"other:admin\"]"));

        var result = await Transformer(new OidcOptions { RolePrefix = "myapp:" })
            .TransformAsync(principal);

        Roles(result).Should().BeEquivalentTo("myapp:admin");
    }

    [Fact]
    public async Task AllowedRolesAndPrefix_MustBothMatch()
    {
        var principal = PrincipalWith(new Claim("roles", "[\"myapp:admin\",\"myapp:stray\",\"other:admin\"]"));

        var result = await Transformer(new OidcOptions { RolePrefix = "myapp:", AllowedRoles = ["myapp:admin"] })
            .TransformAsync(principal);

        Roles(result).Should().BeEquivalentTo("myapp:admin");
    }
}
