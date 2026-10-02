using System.Threading.Tasks;
using Pragmatic.Testing.Assertions;
using Xunit;
using UnusedSuppressor = Pragmatic.SourceGenerator.Suppressors.UnusedMemberSuppressor;

namespace Pragmatic.SourceGenerator.Analyzers.Tests.Suppressors;

/// <summary>
///     PRAGS004 suppresses IDE0051 because "a private member may be used by SG-generated *partial
///     class* code". Without `partial` on the type there is no generated half that could use it.
/// </summary>
public class UnusedMemberSuppressorTests
{
    private static Task<System.Collections.Immutable.ImmutableArray<Microsoft.CodeAnalysis.Diagnostic>> RunAsync(string source)
        => SuppressorTestHarness.RunAsync(
            new UnusedSuppressor(),
            new MemberDiagnosticProducer("IDE0051"),
            (PragmaticAttributeStubs.Path, PragmaticAttributeStubs.Source),
            (SuppressorTestHarness.HandWrittenPath, source));

    // (1) Legitimate case — the generated half of the partial type may call this member.
    [Fact]
    public async Task PrivateMember_OnPartialPragmaticType_IsSuppressed()
    {
        var diagnostics = await RunAsync("""
            [Pragmatic.Actions.Attributes.Query]
            public partial class GetOrders
            {
                private void Helper() { }
            }
            """);

        SuppressorTestHarness.Single(diagnostics, "IDE0051", "Helper").IsSuppressed.Should().BeTrue();
    }

    // (2) Hand-written case — a sealed, non-partial type has no generated half, so a dead private
    // member is genuinely dead and IDE0051 must stay visible.
    [Fact]
    public async Task PrivateMember_OnNonPartialPragmaticType_IsNotSuppressed()
    {
        var diagnostics = await RunAsync("""
            [Pragmatic.Actions.Attributes.Query]
            public class GetCustomers
            {
                private void Helper() { }
            }
            """);

        SuppressorTestHarness.Single(diagnostics, "IDE0051", "Helper").IsSuppressed.Should().BeFalse();
    }
}
