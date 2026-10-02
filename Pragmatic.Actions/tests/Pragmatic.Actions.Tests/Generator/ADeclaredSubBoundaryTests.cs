using System.Linq;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     <c>[SubBoundary(Name = "…")]</c> names the group, where the namespace would infer it.
/// </summary>
/// <remarks>
///     <para>
///         The attribute is the documented way to override the inference. It reaches the field the
///         boundary builder prefers over the namespace — <c>BoundaryMemberModel.SubBoundaryName</c> —
///         which generated operations (<c>[Resource]</c> and the traits) also populate. An attribute
///         that did not reach it would produce no group, no interface and no diagnostic.
///     </para>
///     <para>
///         ⚠️ Which is the case where it is reached for: the namespace inferred the wrong group, or
///         no group at all, and the developer names the right one. Ignored, it would leave them the
///         wrong group and silence.
///     </para>
/// </remarks>
public class ADeclaredSubBoundaryTests : ActionsGeneratorTestBase
{
    private const string CommonUsings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;
        """;

    /// <summary>Two operations in one flat namespace: one names a group, the other does not.</summary>
    private const string Declared = CommonUsings + """

        namespace TestApp.Sales
        {
            [Boundary]
            public partial class SalesBoundary;
        }

        namespace TestApp.Sales.Actions
        {
            [DomainAction(Internal = false)]
            [SubBoundary(Name = "References")]
            public partial class RenameOrderAction : DomainAction<Guid>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }

            [DomainAction(Internal = false)]
            public partial class ArchiveOrderAction : DomainAction<Guid>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }
        }
        """;

    [Fact]
    public void ADeclaredName_MakesTheGroup()
    {
        var result = RunGenerator(Declared);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result) ?? "";

        generated.Should().Contain("interface ISalesReferencesActions",
            "the declared name is the group, exactly as an inferred one would be");
        generated.Should().Contain("RenameOrder(");
    }

    /// <summary>
    ///     The control: the twin in the same namespace, without the attribute, stays on the root.
    /// </summary>
    /// <remarks>
    ///     Without it, "the declared group exists" is satisfied by a change that puts every operation
    ///     of the namespace into it — and the namespace here infers nothing, so every operation would
    ///     otherwise be flat.
    /// </remarks>
    [Fact]
    public void TheTwinWithoutIt_StaysOnTheRoot()
    {
        var root = InterfaceBody(GetBoundarySource(RunGenerator(Declared)) ?? "", "ISalesActions");

        root.Should().Contain("ArchiveOrder(",
            "it declared no group and its namespace infers none");
        root.Should().NotContain("RenameOrder(",
            "and the one that declared a group is not also on the root");
    }

    /// <summary>
    ///     The text of one interface declaration, up to the next one.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Slicing from the root to the group's own declaration is what the first version did, and
    ///     it threw: the generated file declares the group <b>before</b> the root. Reading to the next
    ///     interface, whichever it is, does not depend on an order nothing promises.
    /// </remarks>
    private static string InterfaceBody(string generated, string name)
    {
        var start = generated.IndexOf($"interface {name}", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{name} is generated");

        var next = generated.IndexOf("interface ", start + 10, StringComparison.Ordinal);
        return next < 0 ? generated[start..] : generated[start..next];
    }

    /// <summary>
    ///     A declared group is not an inferred one, so PRAG0413 stays quiet about it.
    /// </summary>
    /// <remarks>
    ///     PRAG0413 exists because the inference turns a folder into public API without anyone writing
    ///     it. An author who wrote the name does not need to be told what they wrote.
    /// </remarks>
    [Fact]
    public void ADeclaredGroup_IsNotReportedAsInferred()
    {
        HasDiagnostic(RunGenerator(Declared), "PRAG0413").Should().BeFalse();
    }

    [Fact]
    public void ADeclaredNameWins_OverTheNamespace()
    {
        var result = RunGenerator(CommonUsings + """

            namespace TestApp.Sales
            {
                [Boundary]
                public partial class SalesBoundary;
            }

            namespace TestApp.Sales.Orders.Actions
            {
                [DomainAction(Internal = false)]
                [SubBoundary(Name = "References")]
                public partial class RenameOrderAction : DomainAction<Guid>
                {
                    public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
                }
            }
            """);

        var generated = GetBoundarySource(result) ?? "";

        generated.Should().Contain("interface ISalesReferencesActions",
            "the namespace says Orders and the author says References; the author wrote one of them");
        generated.Should().NotContain("interface ISalesOrdersActions");
    }

    // ── What a declared name may not be ─────────────────────────────────────────────────────────

    /// <summary>
    ///     A name that is empty, or the boundary's own, is refused.
    /// </summary>
    /// <remarks>
    ///     The rule for an inferred group — never the module's own name, and it must separate
    ///     something — is a rule about the group and not about how it was arrived at. An empty name
    ///     would silently fall back to the inference, which is what declaring a name exists to avoid.
    /// </remarks>
    [Fact]
    public void AnEmptyName_IsReported()
    {
        HasDiagnostic(RunGenerator(Declared.Replace("Name = \"References\"", "Name = \"  \"")), "PRAG0416")
            .Should().BeTrue();
    }

    [Fact]
    public void ANameThatIsTheBoundarysOwn_IsReported()
    {
        HasDiagnostic(RunGenerator(Declared.Replace("Name = \"References\"", "Name = \"Sales\"")), "PRAG0416")
            .Should().BeTrue("a group named after its boundary would generate ISalesSalesActions");
    }

    /// <summary>The control: an ordinary name is silent.</summary>
    [Fact]
    public void AnOrdinaryName_IsNotReported()
    {
        HasDiagnostic(RunGenerator(Declared), "PRAG0416").Should().BeFalse();
    }

    /// <summary>
    ///     A refused name does not take the operation with it.
    /// </summary>
    /// <remarks>
    ///     Answering a bad group name by removing a call from the boundary would be a worse outcome
    ///     than the one being refused. The operation goes where it would have gone without the
    ///     attribute — here, the root.
    /// </remarks>
    [Fact]
    public void ARefusedName_LeavesTheOperationOnTheRoot()
    {
        var generated = GetBoundarySource(
            RunGenerator(Declared.Replace("Name = \"References\"", "Name = \"Sales\""))) ?? "";

        InterfaceBody(generated, "ISalesActions").Should().Contain("RenameOrder(");
        generated.Should().NotContain("ISalesSalesActions");
    }

    // ── Description ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     <c>Description</c> is the group interface's summary.
    /// </summary>
    /// <remarks>
    ///     The second half of the attribute, and it was dead in the same way: the property existed,
    ///     <c>SubBoundaryModel.Description</c> existed to receive it, and nothing ever set or read
    ///     either. Fixing one half and leaving the other is the shape this epic exists to remove.
    /// </remarks>
    [Fact]
    public void ADeclaredDescription_IsTheGroupsSummary()
    {
        var generated = GetBoundarySource(RunGenerator(Declared.Replace(
            "[SubBoundary(Name = \"References\")]",
            "[SubBoundary(Name = \"References\", Description = \"What an order is called.\")]"))) ?? "";

        generated.Should().Contain("What an order is called.");
    }

    /// <summary>The control: with no description, the generated sentence is the one it always was.</summary>
    [Fact]
    public void WithoutADescription_TheDefaultSummaryStands()
    {
        (GetBoundarySource(RunGenerator(Declared)) ?? "")
            .Should().Contain("Groups actions belonging to the References sub-boundary of Sales.");
    }
}
