using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Showcase.Accounts;
using Xunit;

namespace Showcase.Tests.Integration;

/// <summary>
///     An imported package's operations read the database of the boundary the import named.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ A package declares no <c>[Boundary]</c> — that is what makes it a package — so the
///         invokers it generates ask for <c>DbContext</c> and <c>IUnitOfWork</c> <b>unkeyed</b>, and
///         those constructors are fixed in the package's own compilation. <c>DbContext</c> is
///         registered keyed by boundary, so nothing answered them: six operations in
///         <c>Pragmatic.Authorization.Management</c> were in that state and no application imported the
///         package, which is the only reason nothing had failed.
///     </para>
///     <para>
///         The key cannot be pushed into a constructor that is already compiled, so it comes from the
///         composition instead: <c>[UsePackage&lt;TPackage, TBoundary&gt;]</c> on the importing module,
///         and the generated registration answers the unkeyed request from that boundary's keyed
///         instance. <c>Showcase.Accounts</c> is the application that imports it.
///     </para>
/// </remarks>
public sealed class ThePackageUsesTheBoundaryItWasImportedInto
{
    /// <summary>The unkeyed request an imported operation makes is answered by the named boundary.</summary>
    [Fact]
    public void TheUnkeyedRequest_ResolvesTheBoundarysOwnContext()
    {
        var services = new ServiceCollection();
        var expected = new ProbeContext();
        services.AddKeyedScoped<DbContext>(typeof(AccountsBoundary), (_, _) => expected);
        services.AddAccountsBoundary();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<DbContext>().Should().BeSameAs(expected);
    }

    /// <summary>
    ///     The control: it is the boundary's context and not just any context.
    /// </summary>
    /// <remarks>
    ///     Without it, "the unkeyed request resolves" is satisfied by an application that happens to
    ///     register an unkeyed <c>DbContext</c> of its own — which is the reading this defect was open
    ///     against, and the one that would have made the package work by accident in one host and fail
    ///     in the next.
    /// </remarks>
    [Fact]
    public void WithNoContextForThatBoundary_TheResolutionFails()
    {
        var services = new ServiceCollection();
        services.AddKeyedScoped<DbContext>("some-other-key", (_, _) => new ProbeContext());
        services.AddAccountsBoundary();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<DbContext>();

        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    ///     And the operations themselves are on the boundary, resolvable by the interface a caller uses.
    /// </summary>
    /// <remarks>
    ///     The registration and the surface are two facts: an invoker registered under an interface the
    ///     facade does not expose is reachable by nobody.
    /// </remarks>
    [Fact]
    public void TheImportedOperations_AreOnTheBoundarysFacade()
    {
        var services = new ServiceCollection();
        services.AddAccountsBoundary();

        services.Should().Contain(d => d.ServiceType == typeof(IAccountsAuthorizationActions));

        typeof(IAccountsAuthorizationActions).GetMethod("CreatePermission").Should().NotBeNull(
            "the package's operation is fused onto the importing boundary's facade");
    }

    private sealed class ProbeContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseInMemoryDatabase("probe");
    }
}
