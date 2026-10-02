using Pragmatic.Authorization;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

/// <summary>
///     A custom permission is declared one way: <c>[assembly: Permission]</c>. The type-per-permission
///     <c>IPermission</c>, and the <c>[ExplicitPermission&lt;TPermission&gt;]</c> that named one, are gone —
///     removed rather than deprecated, before v1.
/// </summary>
public class ACustomPermissionHasOneDeclarationTests
{
    private static readonly System.Reflection.Assembly Abstractions = typeof(PermissionAttribute).Assembly;

    [Fact]
    public void ThereIsNoIPermission()
        => Abstractions.GetType("Pragmatic.Authorization.IPermission").Should().BeNull();

    [Fact]
    public void AnExplicitPermission_NamesAValue_NotAType()
        => Abstractions.GetType("Pragmatic.Authorization.ExplicitPermissionAttribute`1").Should().BeNull();

    /// <summary>The control: the lookup finds what is there, so the two nulls above are not the lookup failing.</summary>
    [Theory]
    [InlineData("Pragmatic.Authorization.PermissionAttribute")]
    [InlineData("Pragmatic.Authorization.ExplicitPermissionAttribute")]
    [InlineData("Pragmatic.Authorization.IRole")]
    public void TheLookup_FindsWhatRemains(string name)
        => Abstractions.GetType(name).Should().NotBeNull();
}
