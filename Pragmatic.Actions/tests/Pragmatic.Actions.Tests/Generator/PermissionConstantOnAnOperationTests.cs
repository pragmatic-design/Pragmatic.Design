using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     PRAG0418 — <c>[RequirePermission(SomeConstant)]</c> on an operation, where the constant resolves
///     to no permission value.
/// </summary>
/// <remarks>
///     A generator cannot bind a constant it is itself creating, so a written constant path is resolved
///     against the catalogue of what this compilation will generate. A path the catalogue does not know
///     yields no value, the operation enters the requirement registry with nothing, and the check lets
///     everyone through: fail-open, in the one place the author wrote a permission down. The endpoint
///     side has <c>PRAG0528</c> and a test; the operation side had the diagnostic and no test.
/// </remarks>
public class PermissionConstantOnAnOperationTests : ActionsGeneratorTestBase
{
    private const string CommonUsings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Authorization;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;
        """;

    /// <param name="permission">The argument written inside <c>[RequirePermission(...)]</c>.</param>
    private static string Source(string permission) => CommonUsings + $$"""

        namespace TestApp.Billing;

        [Boundary]
        public partial class BillingBoundary;

        [Entity]
        [BelongsTo<BillingBoundary>]
        public partial class Invoice : IEntity
        {
            public Guid PersistenceId { get; set; }
        }

        [DomainAction]
        [RequirePermission({{permission}})]
        public partial class IssueRefundAction : DomainAction<Guid>
        {
            public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
        }
        """;

    /// <summary>
    ///     A constant no producer in this compilation generates. <c>GhostPermissions</c> does not exist,
    ///     which is the shape the defect takes in practice — a renamed entity, a permission class that
    ///     moved — and the generator reads the path from syntax, so it sees it either way.
    /// </summary>
    [Fact]
    public void AConstantNothingGenerates_ReportsPrag0418()
    {
        var result = RunGeneratorWithEntities(Source("GhostPermissions.Read"));

        HasDiagnostic(result, "PRAG0418").Should().BeTrue(
            "an unresolvable constant is enforced as nothing at all, and the build would otherwise be silent");
    }

    /// <summary>
    ///     The control: the constant the entity's own permission class will carry. It resolves through
    ///     the catalogue, so the requirement is real and nothing is reported.
    /// </summary>
    [Fact]
    public void AGeneratedEntityConstant_ResolvesAndReportsNothing()
    {
        var result = RunGeneratorWithEntities(Source("BillingPermissions.Invoice.Read"));

        HasDiagnostic(result, "PRAG0418").Should().BeFalse();
        GetGeneratedSource(result, "PermissionRequirementRegistry")
            .Should().NotBeNull().And.Contain("billing.invoice.read");
    }

    /// <summary>
    ///     A literal beside a generated constant. The constant cannot be bound in this run, and Roslyn then drops
    ///     every constructor argument — the literal's value with it. Read back from the syntax as its quoted
    ///     text, the literal would be looked up in the catalogue as a path and reported as unresolvable.
    /// </summary>
    [Fact]
    public void ALiteralBesideAGeneratedConstant_IsItsValue()
    {
        var result = RunGeneratorWithEntities(Source("\"billing.refund.issue\", BillingPermissions.Invoice.Read"));

        HasDiagnostic(result, "PRAG0418").Should().BeFalse();
        GetGeneratedSource(result, "PermissionRequirementRegistry").Should().NotBeNull()
            .And.Contain("\"billing.refund.issue\"")
            .And.Contain("\"billing.invoice.read\"");
    }

    /// <summary>
    ///     A constant of a referenced assembly beside a generated one. The same drop takes the referenced constant
    ///     with it, and it was captured as a path — <c>Company.Grants.SharedPermissions.Refund</c> — which the
    ///     catalogue does not hold: reported unresolvable, and enforced as nothing where no diagnostic runs.
    /// </summary>
    [Fact]
    public void AReferencedConstantBesideAGeneratedOne_IsItsValue()
    {
        var grants = GeneratorTestHelper.CompileReference("Company.Grants", """
            namespace Company.Grants;

            public static class SharedPermissions
            {
                public const string Refund = "billing.refund.issue";
            }
            """);

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Source("Company.Grants.SharedPermissions.Refund, BillingPermissions.Invoice.Read"),
            [.. GetActionsAndEntityReferences(), grants]);

        HasDiagnostic(result, "PRAG0418").Should().BeFalse();
        GetGeneratedSource(result, "PermissionRequirementRegistry").Should().NotBeNull()
            .And.Contain("\"billing.refund.issue\"")
            .And.Contain("\"billing.invoice.read\"");
    }

    /// <summary>The same for a hand-written constant of this compilation: it binds, but not beside a generated one.</summary>
    [Fact]
    public void AHandWrittenConstantBesideAGeneratedOne_IsItsValue()
    {
        var result = RunGeneratorWithEntities(Source("LocalGrants.Refund, BillingPermissions.Invoice.Read") + """

            public static class LocalGrants
            {
                public const string Refund = "billing.refund.issue";
            }
            """);

        HasDiagnostic(result, "PRAG0418").Should().BeFalse();
        GetGeneratedSource(result, "PermissionRequirementRegistry").Should().NotBeNull()
            .And.Contain("\"billing.refund.issue\"")
            .And.Contain("\"billing.invoice.read\"");
    }

    /// <summary>And a literal, which needs no catalogue at all.</summary>
    [Fact]
    public void ALiteralPermission_ReportsNothing()
    {
        var result = RunGeneratorWithEntities(Source("\"billing.refund.issue\""));

        HasDiagnostic(result, "PRAG0418").Should().BeFalse();
    }
}
