using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Boundary;
using Pragmatic.Actions.EFCore;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     <c>AddBoundary&lt;T&gt;</c> refuses a second registration of the same boundary.
/// </summary>
/// <remarks>
///     <para>
///         Accepting two registrations would mean two configuration singletons, and
///         <c>GetBoundaryConfiguration&lt;T&gt;</c> silently takes the first — so the second call, a
///         <c>UseRemote</c> meant to replace a <c>UseLocal</c>, would do nothing and say nothing. The
///         runtime topology validator is opt-in, so it cannot be the guard.
///     </para>
///     <para>
///         The check lives where the mistake is made, so it holds for every application.
///     </para>
/// </remarks>
public sealed class ABoundaryIsRegisteredOnceTests
{
    private sealed class OrdersBoundary : IBoundary;

    private sealed class BillingBoundary : IBoundary;

    [Fact]
    public void TheSameBoundaryTwice_IsRefused_NamingIt()
    {
        var services = new ServiceCollection();
        services.AddBoundary<OrdersBoundary>(cfg => cfg.UseLocal().UseDatabase(o => o.UseInMemoryDatabase("orders")));

        var act = () => services.AddBoundary<OrdersBoundary>(cfg => cfg.UseRemote("https://orders.example.com"));

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain(nameof(OrdersBoundary));
    }

    /// <summary>The refused call leaves the first registration as it was.</summary>
    [Fact]
    public void TheRefusedSecondCall_LeavesTheFirstRegistration()
    {
        var services = new ServiceCollection();
        services.AddBoundary<OrdersBoundary>(cfg => cfg.UseLocal().UseDatabase(o => o.UseInMemoryDatabase("orders")));

        var act = () => services.AddBoundary<OrdersBoundary>(cfg => cfg.UseRemote("https://orders.example.com"));
        act.Should().Throw<InvalidOperationException>();

        services.GetAllBoundaryConfigurations().Should().ContainSingle()
            .Which.Mode.Should().Be(BoundaryMode.Local);
    }

    /// <summary>The control: two different boundaries are both registered.</summary>
    [Fact]
    public void TwoDifferentBoundaries_AreBothRegistered()
    {
        var services = new ServiceCollection();
        services.AddBoundary<OrdersBoundary>(cfg => cfg.UseLocal().UseDatabase(o => o.UseInMemoryDatabase("orders")));
        services.AddBoundary<BillingBoundary>(cfg => cfg.UseRemote("https://billing.example.com"));

        services.GetAllBoundaryConfigurations().Should().HaveCount(2);
    }
}
