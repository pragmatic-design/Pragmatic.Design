using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     Auto-derivation of action/mutation permissions, behind the opt-in
///     <c>[assembly: PragmaticAutoDerivePermissions]</c> / <c>&lt;PragmaticAutoDerivePermissions&gt;</c>
///     switch. The assertions read the <i>generated</i> permission requirement registry, because that is
///     what <c>PermissionAuthorizationFilter</c> enforces at runtime — a model-level assertion would pass
///     for an action nobody ever checks.
/// </summary>
public class AutoDerivedPermissionTests : ActionsGeneratorTestBase
{
    private const string CommonUsings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Authorization;
        using Pragmatic.Result;
        """;

    /// <summary>
    ///     [AllowAnonymous] lives in Pragmatic.Endpoints, which the Actions test references do not carry.
    ///     The generator matches it by metadata name, so a source-declared one in the same namespace is
    ///     indistinguishable from the shipped attribute.
    /// </summary>
    private const string AllowAnonymousStub = """

        namespace Pragmatic.Endpoints.Attributes
        {
            [AttributeUsage(AttributeTargets.Class, Inherited = false)]
            public sealed class AllowAnonymousAttribute : Attribute { }
        }
        """;

    private const string OptIn = "[assembly: PragmaticAutoDerivePermissions]\n";

    private static string Body(string extra = "") => $$"""

        namespace TestApp.Billing;

        [Boundary]
        public partial class BillingBoundary;

        {{extra}}

        [DomainAction]
        public partial class IssueRefundAction : DomainAction<Guid>
        {
            public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
        }
        """;

    private static string? Registry(SourceGenRunResult result)
        => GetGeneratedSource(result, "PermissionRequirementRegistry");

    // ── the invariant: with the switch off nothing derives ────────────────────────────────────────

    [Fact]
    public void FlagOff_ActionWithoutPermission_EmitsNoPermissionRequirement()
    {
        var result = RunGenerator(CommonUsings + Body());

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));
        // The registry is emitted for every assembly with actions, so "nothing was derived" is not
        // "no registry" — it is a registry that names no action. The stronger assertion: BeNull would
        // also pass when the generator produced nothing at all.
        Registry(result).Should().NotBeNull().And.NotContain("IssueRefundAction",
            "with the switch off an action without [RequirePermission] requires nothing, as before");
    }

    // ── derivation ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FlagOn_ActionWithoutPermission_DerivesBoundaryQualifiedName()
    {
        var result = RunGenerator(CommonUsings + OptIn + Body());

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var registry = Registry(result);
        registry.Should().NotBeNull("the switch is on, so the action must require its derived permission");
        registry!.Should().Contain("\"billing.issue-refund\"")
            .And.Contain("IssueRefundAction");
    }

    // A derived name has the same three-segment shape as a written one when the operation opens with a
    // verb; a flat derived segment would make a role file mix catalog.property.read with
    // booking.addguest-comment. Asserted on the generated registry, not on the naming helper: the helper
    // being right proves nothing about what the action ends up requiring.
    [Fact]
    public void FlagOn_ActionNamedWithALeadingVerb_DerivesTheThreeSegmentShape()
    {
        var action = """

            [DomainAction]
            public partial class AddGuestCommentAction : DomainAction<Guid>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }
            """;

        var result = RunGenerator(CommonUsings + OptIn + Body(action));

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        Registry(result).Should().Contain("\"billing.guest-comment.add\"")
            .And.NotContain("\"billing.addguest-comment\"");
    }

    // The verb list is closed, and everything outside it keeps the shape it had. Without this the change
    // would be free to make an unrecognised name worse and nothing would say so.
    [Fact]
    public void FlagOn_ActionWhoseLeadingWordIsNotAVerb_KeepsTheFlatName()
    {
        var result = RunGenerator(CommonUsings + OptIn + Body());

        Registry(result).Should().Contain("\"billing.issue-refund\"",
            "'Issue' is not in the verb list, so the name keeps its single segment");
    }

    [Fact]
    public void FlagOn_MutationWithoutPermission_DerivesBoundaryQualifiedName()
    {
        var source = CommonUsings + """
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Persistence.Entity;

            """ + OptIn + """

            namespace TestApp.Billing;

            [Boundary]
            public partial class BillingBoundary;

            public class Invoice : IEntity
            {
                public Guid PersistenceId { get; set; }
                public string Status { get; set; } = "Draft";

                internal void SetStatus(string value) => Status = value;
            }

            [Mutation(Mode = MutationMode.Update)]
            public partial class ApproveInvoiceMutation : Mutation<Invoice>
            {
                public string Status { get; init; } = "Approved";
            }
            """;

        var result = RunGeneratorWithEntities(source);

        var registry = Registry(result);
        registry.Should().NotBeNull();
        registry!.Should().Contain("\"billing.approve-invoice\"");
    }

    [Fact]
    public void FlagOn_ActionOutsideAnyBoundary_DerivesBareName()
    {
        var source = CommonUsings + OptIn + """

            namespace TestApp.Loose;

            [DomainAction]
            public partial class ArchiveEverythingAction : DomainAction<Guid>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }
            """;

        var result = RunGenerator(source);

        var registry = Registry(result);
        registry.Should().NotBeNull();
        registry!.Should().Contain("\"archive-everything\"");
    }

    // ── overrides ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FlagOn_ExplicitPermissionString_WinsOverDerivedName()
    {
        var source = CommonUsings + OptIn + """

            namespace TestApp.Billing;

            [Boundary]
            public partial class BillingBoundary;

            [DomainAction]
            [ExplicitPermission("billing.refund.issue")]
            public partial class IssueRefundAction : DomainAction<Guid>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }
            """;

        var result = RunGenerator(source);

        var registry = Registry(result);
        registry.Should().NotBeNull();
        registry!.Should().Contain("\"billing.refund.issue\"")
            .And.NotContain("\"billing.issue-refund\"");
    }

    /// <summary>
    ///     A generated constant — here one an <c>[assembly: Permission]</c> declares — names the permission. It
    ///     replaced <c>[ExplicitPermission&lt;TPermission&gt;]</c>, which named an <c>IPermission</c> type.
    /// </summary>
    [Fact]
    public void FlagOn_ExplicitPermissionConstant_UsesTheDeclaredValue()
    {
        var source = CommonUsings + OptIn + """

            [assembly: Permission("billing.refund.custom", "Issue a custom refund", Category = "Billing")]

            namespace TestApp.Billing;

            [Boundary]
            public partial class BillingBoundary;

            [DomainAction]
            [ExplicitPermission(BillingPermissions.Refund.Custom)]
            public partial class IssueRefundAction : DomainAction<Guid>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0421").Should().BeFalse();
        var registry = Registry(result);
        registry.Should().NotBeNull();
        registry!.Should().Contain("\"billing.refund.custom\"")
            .And.NotContain("\"billing.issue-refund\"");
    }

    /// <summary>
    ///     A constant nothing generates cannot be resolved: the derived name is used — fail-closed — and
    ///     PRAG0421 says the one written was not.
    /// </summary>
    [Fact]
    public void FlagOn_ExplicitPermissionConstantNothingGenerates_ReportsPrag0421()
    {
        var source = CommonUsings + OptIn + """

            namespace TestApp.Billing;

            [Boundary]
            public partial class BillingBoundary;

            [DomainAction]
            [ExplicitPermission(GhostPermissions.Refund.Custom)]
            public partial class IssueRefundAction : DomainAction<Guid>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0421").Should().BeTrue();
        Registry(result).Should().NotBeNull().And.Contain("\"billing.issue-refund\"");
    }

    [Fact]
    public void FlagOn_AllowAnonymous_DerivesNothing()
    {
        var source = CommonUsings + OptIn + AllowAnonymousStub + """

            namespace TestApp.Billing;

            [Boundary]
            public partial class BillingBoundary;

            [DomainAction]
            [Pragmatic.Endpoints.Attributes.AllowAnonymous]
            public partial class IssueRefundAction : DomainAction<Guid>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }
            """;

        var result = RunGenerator(source);

        Registry(result).Should().NotBeNull().And.NotContain("IssueRefundAction",
            "[AllowAnonymous] means no permission at all, not even a derived one");
    }

    [Fact]
    public void FlagOn_ExistingRequirePermission_IsLeftAlone()
    {
        var source = CommonUsings + OptIn + """

            namespace TestApp.Billing;

            [Boundary]
            public partial class BillingBoundary;

            [DomainAction]
            [RequirePermission("billing.written.by.hand")]
            public partial class IssueRefundAction : DomainAction<Guid>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }
            """;

        var result = RunGenerator(source);

        var registry = Registry(result);
        registry.Should().NotBeNull();
        registry!.Should().Contain("\"billing.written.by.hand\"")
            .And.NotContain("\"billing.issue-refund\"");
    }

    // ── seeding: a derived name nobody can grant only denies ───────────────────────────────────────

    [Fact]
    public void FlagOn_DerivedName_IsSeededIntoThePermissionCatalog()
    {
        var result = RunGenerator(CommonUsings + OptIn + Body());

        var catalog = GetGeneratedSource(result, "ActionPermissionCatalog");
        catalog.Should().NotBeNull(
            "a derived permission no role can grant would only ever deny");
        catalog!.Should().Contain("\"billing.issue-refund\"")
            .And.Contain("PermissionInfo");
    }

    [Fact]
    public void FlagOff_EmitsNoPermissionCatalog()
    {
        var result = RunGenerator(CommonUsings + Body());

        GetGeneratedSource(result, "ActionPermissionCatalog").Should().BeNull();
    }
}
