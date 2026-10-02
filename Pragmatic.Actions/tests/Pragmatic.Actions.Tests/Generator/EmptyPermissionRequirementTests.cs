using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     <c>[RequirePermission()]</c> with no arguments.
///     <para>
///         The attribute's own constructor rejects an empty list — "an empty permission set would
///         silently grant access". In a zero-reflection runtime that constructor never runs for an
///         action: the generator reads the attribute from the symbol, extracts nothing, and the type
///         does not enter the permission requirement registry at all. <c>GetRequirement</c> then returns
///         null and <c>PermissionAuthorizationFilter</c> reads that as "no requirement declared" —
///         the exact opposite of what was written.
///     </para>
///     <para>
///         Nothing at runtime can tell the two cases apart, because the distinction is erased before
///         the registry is generated. So it is closed where the information still exists: at compile
///         time, with the same rule the constructor already states.
///     </para>
/// </summary>
public class EmptyPermissionRequirementTests : ActionsGeneratorTestBase
{
    private const string Usings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Authorization;
        using Pragmatic.Result;
        """;

    private static string Source(string attribute) => $$"""
        {{Usings}}

        namespace TestApp.Billing;

        [Boundary]
        public partial class BillingBoundary;

        {{attribute}}
        [DomainAction]
        public partial class IssueRefundAction : DomainAction<Guid>
        {
            public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
        }
        """;

    [Fact]
    public void RequirePermission_WithNoPermissions_ReportsPrag0422()
    {
        var result = RunGenerator(Source("[RequirePermission]"));

        HasDiagnostic(result, "PRAG0422").Should().BeTrue();
    }

    [Fact]
    public void RequirePermission_WithEmptyArgumentList_ReportsPrag0422()
    {
        var result = RunGenerator(Source("[RequirePermission()]"));

        HasDiagnostic(result, "PRAG0422").Should().BeTrue();
    }

    [Fact]
    public void RequireAnyPermission_WithNoPermissions_ReportsPrag0422()
    {
        var result = RunGenerator(Source("[RequireAnyPermission()]"));

        HasDiagnostic(result, "PRAG0422").Should().BeTrue();
    }

    /// <summary>
    ///     Why the diagnostic has to be an error rather than a note: the generated artifact carries no
    ///     trace of the declaration, so there is nothing left for the runtime to enforce.
    /// </summary>
    [Fact]
    public void RequirePermission_WithNoPermissions_LeavesTheActionOutOfTheGeneratedRegistry()
    {
        var result = RunGenerator(Source("[RequirePermission()]"));

        var registry = GetGeneratedSource(result, "PermissionRequirementRegistry");
        (registry is null || !registry.Contains("IssueRefundAction")).Should().BeTrue();
    }

    [Fact]
    public void RequirePermission_WithAPermission_IsNotReported()
    {
        var result = RunGenerator(Source("""[RequirePermission("billing.refund.issue")]"""));

        HasDiagnostic(result, "PRAG0422").Should().BeFalse();
        GetGeneratedSource(result, "PermissionRequirementRegistry")
            .Should().NotBeNull().And.Contain("billing.refund.issue");
    }

    [Fact]
    public void NoPermissionAttributeAtAll_IsNotReported()
    {
        var result = RunGenerator(Source(""));

        HasDiagnostic(result, "PRAG0422").Should().BeFalse();
    }
}
