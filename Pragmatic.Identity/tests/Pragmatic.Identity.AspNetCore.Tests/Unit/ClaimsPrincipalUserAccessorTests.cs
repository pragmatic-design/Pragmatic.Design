using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Testing.Assertions;
using Pragmatic.Authorization;
using Pragmatic.Identity;

namespace Pragmatic.Identity.AspNetCore.Tests.Unit;

public sealed class ClaimsPrincipalUserAccessorTests
{
    // Default IdentityOptions: UserId/DisplayName/Role use the long WS-* claim URIs,
    // while Permission/Tenant use short names (permission / tenant_id).
    private readonly IdentityOptions _optionsValue = new();

    [Fact]
    public void Id_WithUserIdClaim_ReturnsValue()
    {
        var accessor = CreateAccessor(Authenticated(
            new Claim(_optionsValue.UserIdClaimType, "user-1")));

        accessor.Id.Should().Be("user-1");
    }

    [Fact]
    public void Id_WhenNoHttpContext_ReturnsEmptyString()
    {
        var accessor = CreateAccessor(httpContext: null);

        accessor.Id.Should().BeEmpty();
    }

    [Fact]
    public void IsAuthenticated_WithAuthenticatedPrincipal_ReturnsTrue()
    {
        var accessor = CreateAccessor(Authenticated(new Claim("sub", "user-1")));

        accessor.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void IsAuthenticated_WithoutHttpContext_ReturnsFalse()
    {
        var accessor = CreateAccessor(httpContext: null);

        accessor.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void Kind_WhenAuthenticated_ReturnsUser()
    {
        var accessor = CreateAccessor(Authenticated(new Claim("sub", "user-1")));

        accessor.Kind.Should().Be(PrincipalKind.User);
    }

    [Fact]
    public void Kind_WhenUnauthenticated_ReturnsAnonymous()
    {
        var accessor = CreateAccessor(httpContext: null);

        accessor.Kind.Should().Be(PrincipalKind.Anonymous);
    }

    [Fact]
    public void TenantId_WithTenantClaim_ReturnsValue()
    {
        var accessor = CreateAccessor(Authenticated(new Claim("tenant_id", "tenant-A")));

        accessor.TenantId.Should().Be("tenant-A");
    }

    [Fact]
    public void TenantId_WhenAbsent_ReturnsNull()
    {
        var accessor = CreateAccessor(Authenticated(new Claim("sub", "user-1")));

        accessor.TenantId.Should().BeNull();
    }

    [Fact]
    public void DisplayName_WithDisplayNameClaim_ReturnsValue()
    {
        var accessor = CreateAccessor(Authenticated(
            new Claim(_optionsValue.DisplayNameClaimType, "Jane Doe")));

        accessor.DisplayName.Should().Be("Jane Doe");
    }

    [Fact]
    public void DisplayName_WhenAbsent_ReturnsNull()
    {
        var accessor = CreateAccessor(httpContext: null);

        accessor.DisplayName.Should().BeNull();
    }

    [Fact]
    public void Delegation_WithActSubClaim_NamesTheActor()
    {
        var accessor = CreateAccessor(Authenticated(new Claim("act_sub", "admin-1")));

        accessor.Delegation!.ActorId.Should().Be("admin-1");
    }

    [Fact]
    public void Claims_GroupsMultiValuedClaimsByNormalizedType()
    {
        var accessor = CreateAccessor(Authenticated(
            new Claim("role", "admin"),
            new Claim("role", "editor")));

        accessor.Claims.Should().ContainKey("role");
        accessor.Claims["role"].Should().BeEquivalentTo("admin", "editor");
    }

    [Fact]
    public void Claims_WhenNoHttpContext_ReturnsEmpty()
    {
        var accessor = CreateAccessor(httpContext: null);

        accessor.Claims.Should().BeEmpty();
    }

    [Fact]
    public void Claims_AreMemoizedAcrossAccesses()
    {
        var accessor = CreateAccessor(Authenticated(new Claim("role", "admin")));

        var first = accessor.Claims;
        var second = accessor.Claims;

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void Authorization_ResolvesFromServiceProvider()
    {
        var authorization = new UserAuthorizationMock();
        var accessor = CreateAccessor(
            Authenticated(new Claim("sub", "user-1")),
            authorization);

        accessor.Authorization.Should().BeSameAs(authorization);
    }

    private ClaimsPrincipalUserAccessor CreateAccessor(
        HttpContext? httpContext,
        IUserAuthorization? authorization = null)
    {
        var contextAccessor = new HttpContextAccessorMock();
        contextAccessor.HttpContext.Returns(httpContext);

        var serviceProvider = new ServiceProviderMock();
        serviceProvider.GetService.When(typeof(IUserAuthorization))
.Returns(authorization ?? new UserAuthorizationMock());

        return new ClaimsPrincipalUserAccessor(
            contextAccessor,
            Options.Create(_optionsValue),
            serviceProvider);
    }

    private static HttpContext Authenticated(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, authenticationType: "Test");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }
}
