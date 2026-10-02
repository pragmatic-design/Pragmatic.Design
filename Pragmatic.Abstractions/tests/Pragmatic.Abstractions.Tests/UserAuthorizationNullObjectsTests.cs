using Pragmatic.Testing.Assertions;
using Pragmatic.Authorization;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

/// <summary>
///     The two null-objects inherit every check from <see cref="ConstantUserAuthorization"/>:
///     locking their behavior guards against regressions in the base class — especially for
///     <see cref="FullAccessUserAuthorization"/>, whose grant-all is security-sensitive.
/// </summary>
public sealed class UserAuthorizationNullObjectsTests
{
    public static readonly TheoryData<IUserAuthorization, bool> Instances = new()
    {
        { NullUserAuthorization.Instance, false },
        { FullAccessUserAuthorization.Instance, true },
    };

    [Theory]
    [MemberData(nameof(Instances))]
    public void AllChecks_ReturnTheConstantResult(IUserAuthorization authorization, bool expected)
    {
        authorization.HasPermission("any.permission").Should().Be(expected);
        authorization.HasAnyPermission(["a", "b"]).Should().Be(expected);
        authorization.HasAllPermissions(["a", "b"]).Should().Be(expected);
        authorization.IsInRole("admin").Should().Be(expected);
        authorization.IsInGroup("ops").Should().Be(expected);
        authorization.HasScope("openid").Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(Instances))]
    public void Collections_AreAlwaysEmpty(IUserAuthorization authorization, bool _)
    {
        authorization.Roles.Should().BeEmpty();
        authorization.Permissions.Should().BeEmpty();
        authorization.Groups.Should().BeEmpty();
        authorization.Scopes.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Instances))]
    public async Task AsyncDefaults_DelegateToTheSameConstant(IUserAuthorization authorization, bool expected)
    {
        (await authorization.HasPermissionAsync("any.permission")).Should().Be(expected);
        (await authorization.HasAnyPermissionAsync(["a"])).Should().Be(expected);
        (await authorization.HasAllPermissionsAsync(["a"])).Should().Be(expected);
        (await authorization.GetPermissionsAsync()).Should().BeEmpty();
    }
}
