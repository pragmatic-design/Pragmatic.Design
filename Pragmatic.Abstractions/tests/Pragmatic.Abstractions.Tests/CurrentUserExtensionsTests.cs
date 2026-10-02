using Pragmatic.Testing.Assertions;
using Pragmatic.Authorization;
using Pragmatic.Identity;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

public sealed class CurrentUserExtensionsTests
{
    private sealed class StubUser : ICurrentUser
    {
        public string Id { get; init; } = "user-42";
        public string? DisplayName { get; init; }
        public bool IsAuthenticated { get; init; } = true;
        public PrincipalKind Kind => PrincipalKind.User;
        public string? TenantId => null;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims { get; init; } =
            new Dictionary<string, IReadOnlyList<string>>();
        public IUserAuthorization Authorization => NullUserAuthorization.Instance;
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
    }

    [Fact]
    public void IdOrNull_Authenticated_ReturnsId()
        => new StubUser().IdOrNull().Should().Be("user-42");

    [Fact]
    public void IdOrNull_Anonymous_ReturnsNull()
        => AnonymousUser.Instance.IdOrNull().Should().BeNull();

    [Fact]
    public void DisplayNameOrId_WithDisplayName_ReturnsIt()
        => new StubUser { DisplayName = "Ada" }.DisplayNameOrId().Should().Be("Ada");

    [Fact]
    public void DisplayNameOrId_WithoutDisplayName_FallsBackToId()
        => new StubUser().DisplayNameOrId().Should().Be("user-42");

    [Fact]
    public void GetClaim_Present_ReturnsFirstValue()
    {
        var user = new StubUser
        {
            Claims = new Dictionary<string, IReadOnlyList<string>> { ["dept"] = ["sales", "ops"] },
        };

        user.GetClaim("dept").Should().Be("sales");
    }

    [Fact]
    public void GetClaim_AbsentOrEmpty_ReturnsNull()
    {
        new StubUser().GetClaim("dept").Should().BeNull();
        new StubUser
        {
            Claims = new Dictionary<string, IReadOnlyList<string>> { ["dept"] = [] },
        }.GetClaim("dept").Should().BeNull();
    }

    [Fact]
    public void GetClaims_ReturnsAllValues_OrEmptyWhenAbsent()
    {
        var user = new StubUser
        {
            Claims = new Dictionary<string, IReadOnlyList<string>> { ["dept"] = ["sales", "ops"] },
        };

        user.GetClaims("dept").Should().Equal("sales", "ops");
        user.GetClaims("missing").Should().BeEmpty();
    }

    [Fact]
    public void AnonymousUser_ComposesTheNullObjects()
    {
        AnonymousUser.Instance.IsAuthenticated.Should().BeFalse();
        AnonymousUser.Instance.Kind.Should().Be(PrincipalKind.Anonymous);
        AnonymousUser.Instance.Authorization.Should().BeSameAs(NullUserAuthorization.Instance);
        AnonymousUser.Instance.Authentication.Should().BeSameAs(NullAuthenticationContext.Instance);
    }
}
