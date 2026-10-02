using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     A resource policy declared on a query is evaluated before the query runs.
/// </summary>
/// <remarks>
///     <para>
///         The action filter chain cannot do it. <c>PolicyEvaluationFilter</c> is an
///         <c>IActionFilter</c> at Order 210 and that chain is executed by an action or mutation invoker;
///         a query has none — its endpoint binds the parameters and calls <c>IQueryExecutor</c>. Left to
///         the filter, <c>[RequirePolicy&lt;T&gt;]</c> on a query would be read by nobody, and the endpoint
///         carrying it would be as open as one carrying nothing.
///     </para>
///     <para>
///         ⚠️ The declaration is not forbidden: the policy is <b>evaluated</b>, so a diagnostic banning
///         it would forbid a working feature. <c>PRAG0530</c>, the id that once reported it, is retired
///         and not reused.
///     </para>
///     <para>
///         No policy registry is involved. The registry maps a type to an instance for callers that only
///         know the type at run time; this endpoint knows it at generation time, so the policy is a
///         <c>static readonly</c> field — created once, which is what <c>[RequirePolicy&lt;T&gt;]</c>
///         documents.
///     </para>
/// </remarks>
public class ResourcePolicyOnQueryTests : EndpointsGeneratorTestBase
{
    private const string Common = """
        using System;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Authorization.Policy;
        using Pragmatic.Identity;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query.Attributes;

        namespace TestApp;

        public sealed class OwnerOnlyPolicy : ResourcePolicy
        {
            public override bool Evaluate(ICurrentUser user) => user.IsAuthenticated;
        }

        public partial class Invoice : IEntity
        {
            public Guid PersistenceId { get; set; }
            public string Number { get; set; } = "";
        }

        public partial class InvoiceDto
        {
            public Guid Id { get; init; }
            public string Number { get; init; } = "";
        }
        """;

    private const string WithPolicy = """

        [Query<Invoice, InvoiceDto>]
        [RequirePolicy<OwnerOnlyPolicy>]
        [Endpoint(HttpVerb.Get, "api/invoices")]
        public partial class SearchInvoices
        {
            public string? Number { get; init; }
            public int Page { get; init; } = 1;
            public int PageSize { get; init; } = 20;
        }
        """;

    [Fact]
    public void ThePolicyInstanceIsCreatedOnce()
    {
        var generated = GetGeneratedSource(
            RunGeneratorWithPersistence(Common + WithPolicy), "SearchInvoices.Endpoint");

        generated.Should().Contain("static readonly global::Pragmatic.Authorization.Policy.ResourcePolicy")
            .And.Contain("new global::TestApp.OwnerOnlyPolicy()",
                "the type is known at generation time, so no registry lookup is needed");
    }

    [Fact]
    public void AnUnauthenticatedCallerIsRefusedBeforeTheQueryRuns()
    {
        var generated = GetGeneratedSource(
            RunGeneratorWithPersistence(Common + WithPolicy), "SearchInvoices.Endpoint");

        generated.Should().Contain("!__user.IsAuthenticated")
            .And.Contain("httpContext.Response.StatusCode = 401;");
    }

    [Fact]
    public void ADeniedPolicyAnswersForbidden()
    {
        var generated = GetGeneratedSource(
            RunGeneratorWithPersistence(Common + WithPolicy), "SearchInvoices.Endpoint");

        generated.Should().Contain("!__policy.Evaluate(__user)")
            .And.Contain("httpContext.Response.StatusCode = 403;");
    }

    /// <summary>
    ///     And it happens before anything reads the request or touches the database.
    /// </summary>
    [Fact]
    public void ThePolicyRunsBeforeTheHandlerIsInvoked()
    {
        var generated = GetGeneratedSource(
            RunGeneratorWithPersistence(Common + WithPolicy), "SearchInvoices.Endpoint");

        var policyIndex = generated!.IndexOf("__policy.Evaluate", StringComparison.Ordinal);
        var handlerIndex = generated.IndexOf("await handler(", StringComparison.Ordinal);

        policyIndex.Should().BeGreaterThan(-1);
        policyIndex.Should().BeLessThan(handlerIndex,
            "a denied caller must not reach the executor");
    }

    /// <summary>
    ///     A control: a query without the attribute carries none of it.
    /// </summary>
    /// <remarks>
    ///     Every query paying for a policy check it never declared would be the opposite defect, and a
    ///     quieter one.
    /// </remarks>
    [Fact]
    public void AQueryWithoutAPolicy_HasNoCheck()
    {
        var generated = GetGeneratedSource(RunGeneratorWithPersistence(Common + """

            [Query<Invoice, InvoiceDto>]
            [Endpoint(HttpVerb.Get, "api/invoices")]
            public partial class SearchInvoices
            {
                public string? Number { get; init; }
                public int Page { get; init; } = 1;
                public int PageSize { get; init; } = 20;
            }
            """), "SearchInvoices.Endpoint");

        generated.Should().NotContain("__policy");
    }
}
