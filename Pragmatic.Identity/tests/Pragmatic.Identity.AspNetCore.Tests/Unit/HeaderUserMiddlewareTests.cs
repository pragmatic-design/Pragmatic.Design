using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Testing.Assertions;
using Pragmatic.Identity;

namespace Pragmatic.Identity.AspNetCore.Tests.Unit;

public sealed class HeaderUserMiddlewareTests
{
    private readonly IdentityOptions _options = new();

    [Fact]
    public async Task InvokeAsync_OutsideDevelopment_ThrowsInvalidOperation()
    {
        var middleware = CreateMiddleware(environmentName: "Production", out _);
        var context = new DefaultHttpContext();
        context.Request.Headers["X-User-Id"] = "user-1";

        var act = () => middleware.InvokeAsync(context, Options.Create(_options));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Production*");
    }

    [Fact]
    public async Task InvokeAsync_InDevelopment_MapsHeadersIntoClaimsAndSetsUser()
    {
        var middleware = CreateMiddleware(environmentName: "Development", out var nextCalled);
        var context = new DefaultHttpContext();
        context.Request.Headers["X-User-Id"] = "user-1";
        context.Request.Headers["X-User-Name"] = "Jane Doe";
        context.Request.Headers["X-User-Roles"] = "admin, editor";
        context.Request.Headers["X-User-Permissions"] = "orders.read,orders.write";
        context.Request.Headers["X-User-Tenant"] = "tenant-A";
        context.Request.Headers["X-User-Groups"] = "g1,g2";
        context.Request.Headers["X-User-Scopes"] = "region-north, region-south";

        await middleware.InvokeAsync(context, Options.Create(_options));

        nextCalled().Should().BeTrue();
        context.User.Identity!.IsAuthenticated.Should().BeTrue();
        context.User.FindFirst(_options.UserIdClaimType)!.Value.Should().Be("user-1");
        context.User.FindFirst(_options.DisplayNameClaimType)!.Value.Should().Be("Jane Doe");
        context.User.FindAll(_options.RoleClaimType).Select(c => c.Value)
            .Should().BeEquivalentTo("admin", "editor");
        context.User.FindAll(_options.PermissionClaimType).Select(c => c.Value)
            .Should().BeEquivalentTo("orders.read", "orders.write");
        context.User.FindFirst(_options.TenantClaimType)!.Value.Should().Be("tenant-A");
        context.User.FindAll("group").Select(c => c.Value)
            .Should().BeEquivalentTo("g1", "g2");
        // The claim DefaultUserScopeResolver turns into scope:{value}, which is what
        // ComputedScopeFilter matches a DataScopeRule against. Without this header the resolver
        // would read a claim nothing here can produce, and a Computed rule would match nothing in
        // Development or in any test that authenticates by header.
        context.User.FindAll("data-scope").Select(c => c.Value)
            .Should().BeEquivalentTo("region-north", "region-south");
    }

    [Fact]
    public async Task InvokeAsync_InDevelopment_NoUserIdHeader_DoesNotSetAuthenticatedUser()
    {
        var middleware = CreateMiddleware(environmentName: "Development", out var nextCalled);
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context, Options.Create(_options));

        nextCalled().Should().BeTrue();
        context.User.Identity?.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_InDevelopment_MissingOptionalHeaders_OmitsThoseClaims()
    {
        var middleware = CreateMiddleware(environmentName: "Development", out _);
        var context = new DefaultHttpContext();
        context.Request.Headers["X-User-Id"] = "user-1";

        await middleware.InvokeAsync(context, Options.Create(_options));

        context.User.Identity!.IsAuthenticated.Should().BeTrue();
        // Display name defaults to "Unknown" when the header is absent.
        context.User.FindFirst(_options.DisplayNameClaimType)!.Value.Should().Be("Unknown");
        context.User.FindAll(_options.RoleClaimType).Should().BeEmpty();
        context.User.FindFirst(_options.TenantClaimType).Should().BeNull();
    }

    /// <summary>
    ///     <c>X-Act-*</c> makes the request a delegated one, so the path the framework documents first
    ///     is reachable without a real IdP.
    /// </summary>
    /// <remarks>
    ///     Everything downstream of a delegation — composed authority, cache keys, the audit trail —
    ///     reads it off these claims. Until these headers existed nothing in development could produce
    ///     them, so an application found out whether delegation worked only after wiring an issuer.
    /// </remarks>
    [Fact]
    public async Task InvokeAsync_WithActorHeaders_EmitsTheDelegationClaims()
    {
        var middleware = CreateMiddleware(environmentName: "Development", out _);
        var context = new DefaultHttpContext();
        context.Request.Headers["X-User-Id"] = "u-ada";
        context.Request.Headers["X-Act-Id"] = "agent-7";
        context.Request.Headers["X-Act-Kind"] = "Agent";
        context.Request.Headers["X-Act-Policy"] = "GrantScoped";
        context.Request.Headers["X-Act-Purpose"] = "nightly digest";
        context.Request.Headers["X-Act-Grant"] = "g-42";
        context.Request.Headers["X-Act-Chain"] = "agent-1,agent-7";
        context.Request.Headers["X-Act-Permissions"] = "knowledge.knowledge-item.read";
        context.Request.Headers["X-Act-Grant-Permissions"] = "knowledge.knowledge-item.read, knowledge.knowledge-item.create";

        await middleware.InvokeAsync(context, Options.Create(_options));

        context.User.FindFirst(DelegationClaims.ActorSubject)!.Value.Should().Be("agent-7");
        context.User.FindFirst(DelegationClaims.ActorKind)!.Value.Should().Be("Agent");
        context.User.FindFirst(DelegationClaims.Policy)!.Value.Should().Be("GrantScoped");
        context.User.FindFirst(DelegationClaims.Purpose)!.Value.Should().Be("nightly digest");
        context.User.FindFirst(DelegationClaims.GrantId)!.Value.Should().Be("g-42");
        context.User.FindFirst(DelegationClaims.Chain)!.Value.Should().Be("agent-1,agent-7");

        context.User.FindAll(DelegationClaims.ActorPermissions).Select(c => c.Value)
            .Should().BeEquivalentTo("knowledge.knowledge-item.read");
        context.User.FindAll(DelegationClaims.GrantPermissions).Select(c => c.Value)
            .Should().BeEquivalentTo("knowledge.knowledge-item.read", "knowledge.knowledge-item.create");
    }

    /// <summary>
    ///     No actor, no delegation — even when the qualifying headers are present.
    /// </summary>
    /// <remarks>
    ///     A principal carrying a policy and a purpose but no <c>act_sub</c> would read as delegated to
    ///     whatever inspects those claims and as ordinary to whatever inspects the actor. Half a
    ///     delegation is worse than none: it is the state in which two pieces of code disagree about
    ///     whose authority is in force.
    /// </remarks>
    [Fact]
    public async Task InvokeAsync_WithoutAnActor_EmitsNoDelegationClaimsAtAll()
    {
        var middleware = CreateMiddleware(environmentName: "Development", out _);
        var context = new DefaultHttpContext();
        context.Request.Headers["X-User-Id"] = "u-ada";
        context.Request.Headers["X-Act-Policy"] = "SubjectOnly";
        context.Request.Headers["X-Act-Purpose"] = "support";
        context.Request.Headers["X-Act-Permissions"] = "workspaces.workspace.read";

        await middleware.InvokeAsync(context, Options.Create(_options));

        context.User.FindFirst(DelegationClaims.ActorSubject).Should().BeNull();
        context.User.FindFirst(DelegationClaims.Policy).Should().BeNull(
            "a policy without an actor is not a delegation, and emitting it invents one");
        context.User.FindAll(DelegationClaims.ActorPermissions).Should().BeEmpty();
    }

    private static HeaderUserMiddleware CreateMiddleware(string environmentName, out Func<bool> nextCalled)
    {
        var called = false;
        RequestDelegate next = _ =>
        {
            called = true;
            return Task.CompletedTask;
        };
        nextCalled = () => called;

        var env = new HostEnvironmentMock();
        env.EnvironmentName.Returns(environmentName);

        return new HeaderUserMiddleware(next, env, NullLogger<HeaderUserMiddleware>.Instance);
    }
}
