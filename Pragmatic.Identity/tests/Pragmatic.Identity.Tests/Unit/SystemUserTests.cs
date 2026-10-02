using Pragmatic.Testing.Assertions;
using Pragmatic.Authorization;

namespace Pragmatic.Identity.Tests.Unit;

public class SystemUserTests
{
    [Fact]
    public void Instance_IsSingleton()
    {
        var a = SystemUser.Instance;
        var b = SystemUser.Instance;

        a.Should().BeSameAs(b);
    }

    [Fact]
    public void Id_ReturnsSystem()
    {
        SystemUser.Instance.Id.Should().Be("system");
    }

    [Fact]
    public void DisplayName_ReturnsSystem()
    {
        SystemUser.Instance.DisplayName.Should().Be("System");
    }

    [Fact]
    public void IsAuthenticated_ReturnsTrue()
    {
        SystemUser.Instance.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void Kind_ReturnsSystem()
    {
        SystemUser.Instance.Kind.Should().Be(PrincipalKind.System);
    }

    [Fact]
    public void TenantId_ReturnsNull()
    {
        SystemUser.Instance.TenantId.Should().BeNull();
    }

    [Fact]
    public void Claims_ReturnsEmpty()
    {
        SystemUser.Instance.Claims.Should().BeEmpty();
    }

    [Fact]
    public void Delegation_ReturnsNull()
    {
        SystemUser.Instance.Delegation.Should().BeNull();
    }

    [Fact]
    public void Authorization_IsFullAccess()
    {
        SystemUser.Instance.Authorization.Should().BeSameAs(FullAccessUserAuthorization.Instance);
    }

    [Fact]
    public void Authorization_HasPermission_AlwaysReturnsTrue()
    {
        SystemUser.Instance.Authorization.HasPermission("any.permission").Should().BeTrue();
    }

    [Fact]
    public void Authorization_HasAnyPermission_AlwaysReturnsTrue()
    {
        SystemUser.Instance.Authorization.HasAnyPermission(["a", "b"]).Should().BeTrue();
    }

    [Fact]
    public void Authorization_HasAllPermissions_AlwaysReturnsTrue()
    {
        SystemUser.Instance.Authorization.HasAllPermissions(["a", "b"]).Should().BeTrue();
    }

    [Fact]
    public void Authentication_IsNull()
    {
        SystemUser.Instance.Authentication.Should().BeSameAs(NullAuthenticationContext.Instance);
    }

    [Fact]
    public void IdOrNull_ReturnsId()
    {
        // SystemUser is authenticated, so IdOrNull should return "system"
        SystemUser.Instance.IdOrNull().Should().Be("system");
    }

    [Fact]
    public void ImplementsICurrentUser()
    {
        ICurrentUser user = SystemUser.Instance;
        user.Should().NotBeNull();
    }
}
