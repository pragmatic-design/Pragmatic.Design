using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.MultiTenancy.Tests.Unit;

/// <summary>
///     A route that runs in no tenant refuses a request that resolves one, and serves one that does not with no tenant, whatever
///     <c>RequireTenant</c> says (#724). Before it, a platform operation could only be tenant-agnostic, which serves a caller in the tenant
///     it brings.
/// </summary>
public class NoTenantEndpointTests
{
    private sealed class StubResolver(string? tenantId) : ITenantResolver
    {
        public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(tenantId);
    }

    private sealed class EndpointFeature : IEndpointFeature
    {
        public Endpoint? Endpoint { get; set; }
    }

    private static async Task<(int Status, bool ReachedNext, string? Resolved, string Body)> Run(string? tenantId, object? marker)
    {
        var reachedNext = false;
        var options = Options.Create(new MultiTenancyOptions { RequireTenant = true, EnforceTenantClaim = false });
        var middleware = new TenantResolutionMiddleware(
            _ => { reachedNext = true; return Task.CompletedTask; },
            NullLogger<TenantResolutionMiddleware>.Instance,
            options);

        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        if (marker is not null)
            context.Features.Set<IEndpointFeature>(new EndpointFeature
            {
                Endpoint = new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(marker), "platform"),
            });

        var tenantContext = new MutableTenantContext();
        await middleware.InvokeAsync(context, new StubResolver(tenantId), tenantContext).ConfigureAwait(false);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync().ConfigureAwait(false);
        return (context.Response.StatusCode, reachedNext, tenantContext.TenantId, body);
    }

    [Fact]
    public async Task ATenantOnANoTenantRoute_IsRefused()
    {
        var (status, reachedNext, resolved, body) = await Run("acme", NoTenantEndpoint.Instance);

        status.Should().Be(StatusCodes.Status400BadRequest);
        reachedNext.Should().BeFalse("the operation must not run inside the tenant the caller brought");
        resolved.Should().BeNullOrEmpty();
        body.Should().Contain(TenantRefusalCodes.NotAllowed);
    }

    [Fact]
    public async Task NoTenantOnANoTenantRoute_RunsWithNone()
    {
        var (status, reachedNext, resolved, _) = await Run(tenantId: null, NoTenantEndpoint.Instance);

        reachedNext.Should().BeTrue("RequireTenant does not apply to a route that runs in no tenant");
        status.Should().Be(StatusCodes.Status200OK);
        resolved.Should().BeNullOrEmpty();
    }

    /// <summary>The control: a tenant-agnostic route still serves the caller in the tenant it brings.</summary>
    [Fact]
    public async Task ATenantOnATenantAgnosticRoute_IsStillServed()
    {
        var (_, reachedNext, resolved, _) = await Run("acme", TenantAgnosticEndpoint.Instance);

        reachedNext.Should().BeTrue();
        resolved.Should().Be("acme");
    }
}
