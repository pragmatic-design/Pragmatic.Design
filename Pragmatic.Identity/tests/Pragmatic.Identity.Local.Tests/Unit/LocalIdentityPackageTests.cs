using Pragmatic.Testing.Assertions;
using Pragmatic.Composition;
using Pragmatic.Identity.Local;

namespace Pragmatic.Identity.Local.Tests.Unit;

public sealed class LocalIdentityPackageTests
{
    [Fact]
    public void PackageName_ReturnsStableContractName()
        => LocalIdentityPackage.PackageName.Should().Be("Pragmatic.Identity.Local");

    [Fact]
    public void RoutePrefix_ReturnsIdentityLocal()
        => LocalIdentityPackage.RoutePrefix.Should().Be("identity/local");

    [Fact]
    public void Description_IsNotNullOrEmpty()
        => LocalIdentityPackage.Description.Should().NotBeNullOrEmpty();

    [Fact]
    public void ImplementsIPackageDefinitionContract()
        => typeof(IPackageDefinition).IsAssignableFrom(typeof(LocalIdentityPackage)).Should().BeTrue();
}
