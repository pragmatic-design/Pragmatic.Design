using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     The third of the four authorization levels, applied to a query.
/// </summary>
/// <remarks>
///     <para>
///         <c>ResourceAuthorizationFilter</c> sits at Order 250 in the action-filter chain, and a query's
///         invoker does not run that chain. Without a call of its own, an
///         <c>IResourceAuthorizer&lt;T&gt;</c> registered for a query would never be consulted.
///     </para>
///     <para>
///         ⚠️ The type argument names the <b>operation</b>, not the row — the Showcase registers
///         <c>IResourceAuthorizer&lt;RefundInvoiceAction&gt;</c> — so for a query it asks "may this
///         caller run this query with these arguments", which is as well defined for a list as for a
///         single result.
///     </para>
///     <para>
///         Resolved with <c>GetService</c>, never <c>GetRequiredService</c>: nothing on the query
///         declares whether an authorizer exists, because registering one is a DI decision. A query with
///         none must keep working.
///     </para>
/// </remarks>
public class QueryResourceAuthorizationTests : EndpointsGeneratorTestBase
{
    private const string Source = """
        using System;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query.Attributes;

        namespace TestApp;

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

        [Query<Invoice, InvoiceDto>]
        [Endpoint(HttpVerb.Get, "api/invoices")]
        public partial class SearchInvoices
        {
            public string? Number { get; init; }
            public int Page { get; init; } = 1;
            public int PageSize { get; init; } = 20;
        }
        """;

    [Fact]
    public void TheAuthorizerIsResolvedOptionallyAndAskedAboutTheQuery()
    {
        var generated = GetGeneratedSource(RunGeneratorWithPersistence(Source), "SearchInvoices.Endpoint");

        generated.Should()
            .Contain("GetService<global::Pragmatic.Authorization.IResourceAuthorizer<SearchInvoices>>",
                "registering one is a DI decision, so a query without an authorizer must still work")
            .And.Contain("__authorizer.CanAccessAsync(__authUser, query, \"read\", ct)",
                "the query instance carries the arguments the answer depends on");
    }

    /// <summary>A refused caller is answered before anything is read.</summary>
    /// <remarks>
    ///     The read is the invoker's now, so what has to come first is the call to it rather than the
    ///     call to the executor. Resource authorization stays in the handler: it answers "may this
    ///     caller run this query with these arguments", and the arguments are bound here.
    /// </remarks>
    [Fact]
    public void ARefusedCallerNeverReachesTheRead()
    {
        var generated = GetGeneratedSource(RunGeneratorWithPersistence(Source), "SearchInvoices.Endpoint");

        generated.Should().Contain("Results.StatusCode(403)");

        var authIndex = generated!.IndexOf("__authorizer.CanAccessAsync", StringComparison.Ordinal);
        var readIndex = generated.IndexOf("__invoker.RunAsync", StringComparison.Ordinal);

        authIndex.Should().BeGreaterThan(-1);
        readIndex.Should().BeGreaterThan(-1);
        authIndex.Should().BeLessThan(readIndex,
            "authorizing after the rows have been read is not authorizing");
    }
}
