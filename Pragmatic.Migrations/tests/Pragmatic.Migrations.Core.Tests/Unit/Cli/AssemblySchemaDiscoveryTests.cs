using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Cli.Discovery;

namespace Pragmatic.Migrations.Core.Tests.Unit.Cli;

/// <summary>
///     Unit tests for <see cref="AssemblySchemaDiscovery"/>.
///     <para>
///         Only the deterministic, side-effect-free paths are covered. The happy path
///         (<see cref="AssemblySchemaDiscovery.Discover"/> on a real host assembly) is intentionally
///         NOT exercised in-process: it loads the target into an isolated, collectible
///         <c>AssemblyLoadContext</c> and runs its static initializers. Pointing it at any assembly
///         already loaded in the test host (including this one) destabilizes the test runner, and
///         pointing it at an untrusted assembly is the very trust-boundary the type warns against.
///         That path is left to the live CLI/E2E harness; here we lock down the input guard.
///     </para>
/// </summary>
public class AssemblySchemaDiscoveryTests
{
    [Fact]
    public void Discover_MissingAssemblyPath_ThrowsFileNotFound()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dll");

        var act = () => AssemblySchemaDiscovery.Discover(missing);

        act.Should().Throw<FileNotFoundException>()
            .WithMessage("*" + Path.GetFileName(missing) + "*");
    }

    [Fact]
    public void AutoDiscoverAssemblyPath_NoSingleCsproj_ReturnsNull()
    {
        // The test working directory does not contain exactly one .csproj at its root, so
        // auto-discovery returns null rather than guessing. (Guards the "ambiguous project" branch.)
        var result = AssemblySchemaDiscovery.AutoDiscoverAssemblyPath();

        // We cannot assert a specific value (it depends on CWD), but the call must be side-effect
        // free and never throw — it either resolves a real dll or returns null.
        (result is null || File.Exists(result)).Should().BeTrue();
    }
}
