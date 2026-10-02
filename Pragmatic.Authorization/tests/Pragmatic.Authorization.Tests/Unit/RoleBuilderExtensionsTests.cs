using Pragmatic.Testing.Assertions;
using Pragmatic.Authorization.Configuration;

namespace Pragmatic.Authorization.Tests.Unit;

public class RoleBuilderExtensionsTests
{
    // =========================================================================
    // WithAllPermissions()
    // =========================================================================

    [Fact]
    public void WithAllPermissions_AddsWildcard()
    {
        var rb = new RoleBuilder();
        rb.WithAllPermissions();

        rb.Resolve().Should().Contain("*");
    }

    [Fact]
    public void WithAllPermissions_CombinesWithExisting()
    {
        var rb = new RoleBuilder();
        rb.WithPermissions("orders.read");
        rb.WithAllPermissions();

        var result = rb.Resolve().ToList();
        result.Should().Contain("orders.read");
        result.Should().Contain("*");
    }

    // =========================================================================
    // WithAllPermissions<TBoundary>()
    // =========================================================================

    // ReSharper disable once ClassNeverInstantiated.Local
    private sealed class BookingBoundary;

    // ReSharper disable once ClassNeverInstantiated.Local
    private sealed class BillingBoundary;

    // ReSharper disable once ClassNeverInstantiated.Local
    private sealed class Catalog;

    [Fact]
    public void WithAllPermissions_Boundary_AddsBoundaryWildcard()
    {
        var rb = new RoleBuilder();
        rb.WithAllPermissions<BookingBoundary>();

        rb.Resolve().Should().Contain("booking.*");
    }

    [Fact]
    public void WithAllPermissions_Boundary_StripsSuffix()
    {
        var rb = new RoleBuilder();
        rb.WithAllPermissions<BillingBoundary>();

        rb.Resolve().Should().Contain("billing.*");
    }

    [Fact]
    public void WithAllPermissions_Boundary_NoSuffix_UsesFullName()
    {
        var rb = new RoleBuilder();
        rb.WithAllPermissions<Catalog>();

        rb.Resolve().Should().Contain("catalog.*");
    }

    [Fact]
    public void WithAllPermissions_MultipleBoundaries()
    {
        var rb = new RoleBuilder();
        rb.WithAllPermissions<BookingBoundary>();
        rb.WithAllPermissions<BillingBoundary>();

        var result = rb.Resolve().ToList();
        result.Should().Contain("booking.*");
        result.Should().Contain("billing.*");
    }

    // =========================================================================
    // WithoutPermissions — wildcard conflict detection
    // =========================================================================

    [Fact]
    public void WithoutPermissions_WildcardConflict_ThrowsAtStartup()
    {
        var rb = new RoleBuilder();
        rb.WithAllPermissions<BookingBoundary>();
        rb.WithoutPermissions("booking.reservation.delete");

        var act = () => rb.Resolve().ToList();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot exclude*booking.reservation.delete*wildcard*booking.*");
    }

    [Fact]
    public void WithoutPermissions_GlobalWildcardConflict_ThrowsAtStartup()
    {
        var rb = new RoleBuilder();
        rb.WithAllPermissions(); // adds "*"
        rb.WithoutPermissions("orders.read");

        var act = () => rb.Resolve().ToList();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot exclude*orders.read*wildcard*");
    }

    [Fact]
    public void WithoutPermissions_ExactMatch_WorksCorrectly()
    {
        var rb = new RoleBuilder();
        rb.WithPermissions("orders.read", "orders.write", "billing.read");
        rb.WithoutPermissions("orders.write");

        var result = rb.Resolve().ToList();
        result.Should().Contain("orders.read");
        result.Should().Contain("billing.read");
        result.Should().NotContain("orders.write");
    }

    [Fact]
    public void WithoutPermissions_NoConflict_WithExplicitPerms()
    {
        var rb = new RoleBuilder();
        rb.WithPermissions("booking.reservation.read", "booking.reservation.create", "identity.login");
        rb.WithoutPermissions("booking.reservation.create");

        var result = rb.Resolve().ToList();
        result.Should().Contain("booking.reservation.read");
        result.Should().Contain("identity.login");
        result.Should().NotContain("booking.reservation.create");
    }
}
