using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.MultiTenancy.Tests;

/// <summary>
///     The effective tenant context prefers the request-scoped tenant and falls back to the
///     ambient <see cref="TenantScope"/> (AsyncLocal), so background jobs / tests using
///     <c>TenantScope.BeginScope</c> are honored.
/// </summary>
public class AmbientTenantContextTests
{
    [Fact]
    public void UnresolvedRequest_FallsBackToTenantScope()
    {
        var request = new MutableTenantContext(); // no HTTP tenant
        var ctx = new AmbientTenantContext(request);

        using (TenantScope.BeginScope("bg-tenant", "Background Tenant"))
        {
            ctx.IsResolved.Should().BeTrue();
            ctx.TenantId.Should().Be("bg-tenant");
            ctx.TenantName.Should().Be("Background Tenant");
        }

        // Scope disposed → ambient cleared, and the request had none.
        ctx.IsResolved.Should().BeFalse();
        ctx.TenantId.Should().BeNull();
    }

    [Fact]
    public void ResolvedRequest_WinsOverAmbientScope()
    {
        var request = new MutableTenantContext { TenantId = "http-tenant" };
        var ctx = new AmbientTenantContext(request);

        using (TenantScope.BeginScope("bg-tenant"))
        {
            ctx.TenantId.Should().Be("http-tenant"); // request-scoped wins
        }
    }
}
