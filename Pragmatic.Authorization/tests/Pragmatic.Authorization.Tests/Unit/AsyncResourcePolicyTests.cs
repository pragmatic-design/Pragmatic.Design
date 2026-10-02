using Pragmatic.Testing.Assertions;
using Pragmatic.Authorization.Policy;
using Pragmatic.Identity;
using static Pragmatic.Authorization.Tests.Unit.PolicyTestHelpers;

namespace Pragmatic.Authorization.Tests.Unit;

public class AsyncResourcePolicyTests
{
    // =========================================================================
    // Sync to Async bridge
    // =========================================================================

    [Fact]
    public async Task SyncToAsync_ImplicitConversion_Works()
    {
        var auth = new FakeUserAuthorization(permissions: ["read"]);
        var user = CreateUser(authorization: auth);

        AsyncResourcePolicy asyncPolicy = ResourcePolicy.RequirePermission("read");
        (await asyncPolicy.EvaluateAsync(user)).Should().BeTrue();
    }

    [Fact]
    public async Task SyncToAsync_ImplicitConversion_Deny()
    {
        var auth = new FakeUserAuthorization();
        var user = CreateUser(authorization: auth);

        AsyncResourcePolicy asyncPolicy = ResourcePolicy.RequirePermission("read");
        (await asyncPolicy.EvaluateAsync(user)).Should().BeFalse();
    }

    // =========================================================================
    // RequireExternalPermission
    // =========================================================================

    [Fact]
    public async Task RequireExternalPermission_DelegateTrue_ReturnsTrue()
    {
        var user = CreateUser();
        var policy = AsyncResourcePolicy.RequireExternalPermission(
            (u, _) => ValueTask.FromResult(u.IsAuthenticated));
        (await policy.EvaluateAsync(user)).Should().BeTrue();
    }

    [Fact]
    public async Task RequireExternalPermission_DelegateFalse_ReturnsFalse()
    {
        var user = CreateUser(isAuthenticated: false);
        var policy = AsyncResourcePolicy.RequireExternalPermission(
            (u, _) => ValueTask.FromResult(u.IsAuthenticated));
        (await policy.EvaluateAsync(user)).Should().BeFalse();
    }

    // =========================================================================
    // Async AND
    // =========================================================================

    [Fact]
    public async Task AsyncAnd_BothTrue_ReturnsTrue()
    {
        var auth = new FakeUserAuthorization(permissions: ["a", "b"]);
        var user = CreateUser(authorization: auth);

        AsyncResourcePolicy left = ResourcePolicy.RequirePermission("a");
        AsyncResourcePolicy right = ResourcePolicy.RequirePermission("b");
        var policy = left & right;

        (await policy.EvaluateAsync(user)).Should().BeTrue();
    }

    [Fact]
    public async Task AsyncAnd_LeftFalse_ShortCircuits()
    {
        var callCount = 0;
        var user = CreateUser(isAuthenticated: false);

        AsyncResourcePolicy left = ResourcePolicy.IsAuthenticated();
        var right = AsyncResourcePolicy.RequireExternalPermission((_, _) =>
        {
            callCount++;
            return ValueTask.FromResult(true);
        });

        var policy = left & right;
        (await policy.EvaluateAsync(user)).Should().BeFalse();
        callCount.Should().Be(0);
    }

    // =========================================================================
    // Async OR
    // =========================================================================

    [Fact]
    public async Task AsyncOr_LeftTrue_ShortCircuits()
    {
        var callCount = 0;
        var user = CreateUser(isAuthenticated: true);

        AsyncResourcePolicy left = ResourcePolicy.IsAuthenticated();
        var right = AsyncResourcePolicy.RequireExternalPermission((_, _) =>
        {
            callCount++;
            return ValueTask.FromResult(false);
        });

        var policy = left | right;
        (await policy.EvaluateAsync(user)).Should().BeTrue();
        callCount.Should().Be(0);
    }

    [Fact]
    public async Task AsyncOr_BothFalse_ReturnsFalse()
    {
        var user = CreateUser(isAuthenticated: false);

        AsyncResourcePolicy left = ResourcePolicy.IsAuthenticated();
        AsyncResourcePolicy right = ResourcePolicy.Deny;

        var policy = left | right;
        (await policy.EvaluateAsync(user)).Should().BeFalse();
    }

    // =========================================================================
    // Async NOT
    // =========================================================================

    [Fact]
    public async Task AsyncNot_InvertsTrue()
    {
        var user = CreateUser(isAuthenticated: true);
        AsyncResourcePolicy inner = ResourcePolicy.IsAuthenticated();
        var policy = !inner;
        (await policy.EvaluateAsync(user)).Should().BeFalse();
    }

    [Fact]
    public async Task AsyncNot_InvertsFalse()
    {
        var user = CreateUser(isAuthenticated: false);
        AsyncResourcePolicy inner = ResourcePolicy.IsAuthenticated();
        var policy = !inner;
        (await policy.EvaluateAsync(user)).Should().BeTrue();
    }

    // =========================================================================
    // Nested async composition
    // =========================================================================

    [Fact]
    public async Task NestedAsyncComposition_Works()
    {
        // (isAuthenticated AND externalCheck) OR syncPermission
        var auth = new FakeUserAuthorization(permissions: ["fallback"]);
        var user = CreateUser(isAuthenticated: false, authorization: auth);

        AsyncResourcePolicy isAuth = ResourcePolicy.IsAuthenticated();
        var externalCheck = AsyncResourcePolicy.RequireExternalPermission(
            (_, _) => ValueTask.FromResult(true));
        AsyncResourcePolicy hasFallback = ResourcePolicy.RequirePermission("fallback");

        var policy = (isAuth & externalCheck) | hasFallback;
        (await policy.EvaluateAsync(user)).Should().BeTrue();
    }

    // =========================================================================
    // CancellationToken propagation
    // =========================================================================

    [Fact]
    public async Task CancellationToken_Propagated()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken capturedToken = default;

        var policy = AsyncResourcePolicy.RequireExternalPermission((_, ct) =>
        {
            capturedToken = ct;
            return ValueTask.FromResult(true);
        });

        var user = CreateUser();
        await policy.EvaluateAsync(user, cts.Token);
        capturedToken.Should().Be(cts.Token);
    }
}
