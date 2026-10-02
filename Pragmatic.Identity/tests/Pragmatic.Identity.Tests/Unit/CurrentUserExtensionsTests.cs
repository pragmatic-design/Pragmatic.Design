using Pragmatic.Testing.Assertions;
using Pragmatic.Authorization;

namespace Pragmatic.Identity.Tests.Unit;

public class CurrentUserExtensionsTests
{
    private sealed class TestUser(
        bool isAuthenticated,
        Dictionary<string, IReadOnlyList<string>>? claims = null) : ICurrentUser
    {
        public string Id => isAuthenticated ? "user-1" : string.Empty;
        public string? DisplayName => isAuthenticated ? "Test User" : null;
        public bool IsAuthenticated => isAuthenticated;
        public PrincipalKind Kind => isAuthenticated ? PrincipalKind.User : PrincipalKind.Anonymous;
        public string? TenantId => null;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims =>
            claims ?? new Dictionary<string, IReadOnlyList<string>>();
        public IUserAuthorization Authorization => NullUserAuthorization.Instance;
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
    }

    // =========================================================================
    // IdOrNull
    // =========================================================================

    [Fact]
    public void IdOrNull_Authenticated_ReturnsId()
    {
        var user = new TestUser(true);
        user.IdOrNull().Should().Be("user-1");
    }

    [Fact]
    public void IdOrNull_Anonymous_ReturnsNull()
    {
        var user = new TestUser(false);
        user.IdOrNull().Should().BeNull();
    }

    // =========================================================================
    // DisplayNameOrId
    // =========================================================================

    [Fact]
    public void DisplayNameOrId_HasDisplayName_ReturnsDisplayName()
    {
        var user = new TestUser(true);
        user.DisplayNameOrId().Should().Be("Test User");
    }

    [Fact]
    public void DisplayNameOrId_NoDisplayName_ReturnsId()
    {
        var user = new TestUser(false);
        user.DisplayNameOrId().Should().Be(string.Empty);
    }

    // =========================================================================
    // GetClaim
    // =========================================================================

    [Fact]
    public void GetClaim_ExistingClaim_ReturnsFirstValue()
    {
        var claims = new Dictionary<string, IReadOnlyList<string>>
        {
            ["plan"] = new List<string> { "premium", "legacy" }
        };
        var user = new TestUser(true, claims);

        user.GetClaim("plan").Should().Be("premium");
    }

    [Fact]
    public void GetClaim_MissingClaim_ReturnsNull()
    {
        var user = new TestUser(true);
        user.GetClaim("nonexistent").Should().BeNull();
    }

    [Fact]
    public void GetClaim_EmptyValues_ReturnsNull()
    {
        var claims = new Dictionary<string, IReadOnlyList<string>>
        {
            ["plan"] = new List<string>()
        };
        var user = new TestUser(true, claims);

        user.GetClaim("plan").Should().BeNull();
    }

    // =========================================================================
    // GetClaims
    // =========================================================================

    [Fact]
    public void GetClaims_ExistingClaim_ReturnsAllValues()
    {
        var claims = new Dictionary<string, IReadOnlyList<string>>
        {
            ["role"] = new List<string> { "admin", "manager" }
        };
        var user = new TestUser(true, claims);

        user.GetClaims("role").Should().HaveCount(2);
        user.GetClaims("role").Should().Contain("admin").And.Contain("manager");
    }

    [Fact]
    public void GetClaims_MissingClaim_ReturnsEmpty()
    {
        var user = new TestUser(true);
        user.GetClaims("nonexistent").Should().BeEmpty();
    }
}
