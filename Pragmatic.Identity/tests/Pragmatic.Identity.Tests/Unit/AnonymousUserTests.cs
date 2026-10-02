using Pragmatic.Testing.Assertions;
using Pragmatic.Authorization;

namespace Pragmatic.Identity.Tests.Unit;

public class AnonymousUserTests
{
    [Fact]
    public void Instance_IsSingleton()
    {
        AnonymousUser.Instance.Should().BeSameAs(AnonymousUser.Instance);
    }

    [Fact]
    public void Id_IsEmpty()
    {
        AnonymousUser.Instance.Id.Should().BeEmpty();
    }

    [Fact]
    public void DisplayName_IsNull()
    {
        AnonymousUser.Instance.DisplayName.Should().BeNull();
    }

    [Fact]
    public void IsAuthenticated_IsFalse()
    {
        AnonymousUser.Instance.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void Kind_IsAnonymous()
    {
        AnonymousUser.Instance.Kind.Should().Be(PrincipalKind.Anonymous);
    }

    [Fact]
    public void TenantId_IsNull()
    {
        AnonymousUser.Instance.TenantId.Should().BeNull();
    }

    [Fact]
    public void Claims_IsEmpty()
    {
        AnonymousUser.Instance.Claims.Should().BeEmpty();
    }

    [Fact]
    public void Delegation_IsNull()
    {
        AnonymousUser.Instance.Delegation.Should().BeNull();
    }

    [Fact]
    public void Authorization_IsNullUserAuthorization()
    {
        AnonymousUser.Instance.Authorization.Should().BeSameAs(NullUserAuthorization.Instance);
    }

    [Fact]
    public void Authorization_HasPermission_ReturnsFalse()
    {
        AnonymousUser.Instance.Authorization.HasPermission("any").Should().BeFalse();
    }

    [Fact]
    public void Authentication_IsNullAuthenticationContext()
    {
        AnonymousUser.Instance.Authentication.Should().BeSameAs(NullAuthenticationContext.Instance);
    }

    [Fact]
    public void ImplementsICurrentUser()
    {
        ICurrentUser user = AnonymousUser.Instance;
        user.Should().NotBeNull();
    }
}
