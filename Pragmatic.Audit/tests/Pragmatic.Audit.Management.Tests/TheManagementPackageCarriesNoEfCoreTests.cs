using Pragmatic.Testing.Assertions;

namespace Pragmatic.Audit.Management.Tests;

/// <summary>
///     The trail's management package needs only <see cref="IAuditRetention" />, which EF Core's trail and the engines' each implement (#553). A
///     host on the Sql or documents engine offers the retention operation through it, so it must not bring EF Core with it.
/// </summary>
/// <remarks>
///     Measured on what a consumer receives, not on the package's own metadata: the compiler drops a reference no type uses, so the assembly
///     listed no EF Core even while the project brought it in. This suite references the package alone, so its output is the package's
///     closure.
/// </remarks>
public class TheManagementPackageCarriesNoEfCoreTests
{
    [Fact]
    public void A_consumer_of_the_management_package_receives_neither_EF_Core_nor_the_EF_Core_trail()
    {
        var delivered = Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll").Select(Path.GetFileName).ToList();

        delivered.Should().Contain("Pragmatic.Audit.Management.dll", "the control: the package itself is in the output");
        delivered.Where(name => name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                                || name.Equals("Pragmatic.Audit.EFCore.dll", StringComparison.Ordinal))
            .Should().BeEmpty();
    }
}
