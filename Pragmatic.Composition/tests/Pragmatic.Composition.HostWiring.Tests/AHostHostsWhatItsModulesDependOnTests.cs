// Pragmatic.Composition.HostWiring.Tests - A hosted module's dependency is hosted or remote

using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A host that hosts a module must host, or declare remote, every module that module depends on —
///     otherwise PRAG1603.
/// </summary>
/// <remarks>
///     <para>
///         The generated host registers exactly what it declares — <c>[Include&lt;T&gt;]</c>,
///         <c>[RemoteBoundary&lt;T&gt;]</c>, its local modules — and does not follow a module's
///         <c>[IncludeModule&lt;T&gt;]</c>. A host that includes Payments, which depends on Ledger, and says
///         nothing about Ledger compiled and failed when Ledger was first resolved. PRAG1601 does not cover
///         it: it asks whether the dependency names a module, not whether the host hosts it. There is no
///         runtime validator: this is decided by the host's generator.
///     </para>
///     <para>
///         Two module assemblies, because the gap is only there across assemblies: a dependency in the
///         same assembly comes in with the module that includes it.
///     </para>
/// </remarks>
public sealed class AHostHostsWhatItsModulesDependOnTests
{
    private const string Ledger = """
        using Pragmatic.Composition.Attributes;

        namespace Ledger
        {
            [Module(Name = "Ledger")]
            public sealed class LedgerModule;
        }
        """;

    private const string Payments = """
        using Pragmatic.Composition.Attributes;

        namespace Payments
        {
            [Module(Name = "Payments")]
            [IncludeModule<Ledger.LedgerModule>]
            public sealed class PaymentsModule;
        }
        """;

    private static string Host(string declarations) => $$"""
        using System.Threading.Tasks;
        using Pragmatic.Composition.Attributes;

        namespace Shop.Host;

        [Module]
        {{declarations}}
        public sealed class ShopHostModule;

        internal static class Program
        {
            private static Task Main(string[] args) => Task.CompletedTask;
        }
        """;

    private static IReadOnlyList<(string, string[])> Modules() =>
    [
        ("Ledger", [Ledger]),
        ("Payments", [Payments])
    ];

    private static List<Microsoft.CodeAnalysis.Diagnostic> Prag1603(string hostDeclarations)
    {
        var (_, diagnostics) = ModuleAndHost.GenerateChain(Modules(), "Shop.Host", Host(hostDeclarations));
        return [.. diagnostics.Where(d => d.Id == "PRAG1603")];
    }

    [Fact]
    public void HostingAModule_WithoutWhatItDependsOn_IsPRAG1603_NamingBoth()
    {
        var reported = Prag1603("[Include<Payments.PaymentsModule>]");

        reported.Should().ContainSingle("the host hosts Payments and says nothing about Ledger");
        var message = reported[0].GetMessage();
        message.Should().Contain("Payments").And.Contain("Ledger");
    }

    /// <summary>The control: the host includes the dependency too.</summary>
    [Fact]
    public void IncludingTheDependency_IsSilent()
        => Prag1603("[Include<Payments.PaymentsModule>]\n[Include<Ledger.LedgerModule>]").Should().BeEmpty();

    /// <summary>The control: the dependency runs elsewhere, declared remote.</summary>
    [Fact]
    public void DeclaringTheDependencyRemote_IsSilent()
        => Prag1603("[Include<Payments.PaymentsModule>]\n[RemoteBoundary<Ledger.LedgerModule>]").Should().BeEmpty();

    /// <summary>The control: a host that declares no topology hosts everything it references.</summary>
    [Fact]
    public void AHostWithNoTopologyDeclarations_IsSilent()
        => Prag1603("").Should().BeEmpty();
}
