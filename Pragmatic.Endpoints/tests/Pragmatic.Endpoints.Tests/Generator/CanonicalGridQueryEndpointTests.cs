using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     A query that takes what a data grid asks for gets a route that can carry it.
/// </summary>
/// <remarks>
///     <para>
///         The canonical <c>GridFilterRequest</c> is a nested object: it has a list of clauses, a list of
///         sorts and a page. A query string cannot express one, so the route has to be a verb with a
///         body — and a query's endpoint was mapped as <c>MapGet</c> whatever the author declared.
///     </para>
///     <para>
///         ⚠️ The verb was not merely defaulted, it was <b>ignored</b>: the model carried the declared
///         one, so the manifest, the route constants and the OpenAPI document all published POST while
///         the route answered GET on the same path. The published contract said what the API does not do.
///     </para>
/// </remarks>
public class CanonicalGridQueryEndpointTests : EndpointsGeneratorTestBase
{
    private const string Common = """
        using System;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query.Adapters;
        using Pragmatic.Persistence.Query.Attributes;

        namespace TestApp;

        public partial class Invoice : IEntity
        {
            public Guid PersistenceId { get; set; }
            public string Number { get; set; } = "";
        }
        """;

    private const string GridQuery = """

        [Query<Invoice, Invoice>]
        [Endpoint(HttpVerb.Post, "api/invoices/grid")]
        public partial class InvoiceGridQuery
        {
            public GridFilterRequest? Grid { get; init; }
        }
        """;

    /// <summary>The route is the verb the author declared.</summary>
    [Fact]
    public void TheDeclaredVerb_IsTheOneMapped()
    {
        var generated = GetGeneratedSource(
            RunGeneratorWithPersistence(Common + GridQuery), "InvoiceGridQuery.Endpoint");

        generated.Should().Contain("MapPost(\"/api/invoices/grid\"",
            "the query declared POST, which is the only verb that can carry the request");
        generated.Should().NotContain("MapGet(\"/api/invoices/grid\"");
    }

    /// <summary>And the request arrives from the body, into the query's own property.</summary>
    [Fact]
    public void TheGridRequest_ArrivesFromTheBody()
    {
        var generated = GetGeneratedSource(
            RunGeneratorWithPersistence(Common + GridQuery), "InvoiceGridQuery.Endpoint");

        generated.Should().Contain("ReadFromJsonAsync",
            "a nested object has nowhere to travel but the body");
        generated.Should().Contain("Grid = grid",
            "and it reaches the property the query declared");
    }

    /// <summary>
    ///     The other half of the pair: a query that declares GET is still mapped as GET.
    /// </summary>
    /// <remarks>
    ///     Without this case, "the declared verb is honoured" would be satisfied by a template that
    ///     always maps POST — and every list endpoint in every application is a GET.
    /// </remarks>
    [Fact]
    public void AQueryThatDeclaresGet_IsStillAGet()
    {
        var generated = GetGeneratedSource(
            RunGeneratorWithPersistence(Common + """

                [Query<Invoice, Invoice>]
                [Endpoint(HttpVerb.Get, "api/invoices")]
                public partial class InvoiceListQuery
                {
                    public string? Number { get; init; }
                }
                """),
            "InvoiceListQuery.Endpoint");

        generated.Should().Contain("MapGet(\"/api/invoices\"");
    }

    /// <summary>The published contract says the route reads a body, and what shape it is.</summary>
    [Fact]
    public void TheContract_SaysWhatTheRouteAccepts()
    {
        var generated = GetGeneratedSource(
            RunGeneratorWithPersistence(Common + GridQuery), "InvoiceGridQuery.Endpoint");

        generated.Should().Contain("PragmaticAcceptsMetadata",
            "a client generated from the document has to know there is a body to send");
        generated.Should().Contain("GridFilterRequest");
    }

    /// <summary>
    ///     A grid request on a GET is refused, by the rule that already covers the shape.
    /// </summary>
    /// <remarks>
    ///     PRAG0532 — "a GET cannot carry this property" — was written for operations and applies here
    ///     unchanged: the request is a nested object and a GET has no body to put it in. Said rather
    ///     than dropped, which would leave a route quietly ignoring the only thing it exists to read.
    /// </remarks>
    [Fact]
    public void AGridRequestOnAGet_IsRefused()
    {
        var result = RunGeneratorWithPersistence(Common + """

            [Query<Invoice, Invoice>]
            [Endpoint(HttpVerb.Get, "api/invoices/grid")]
            public partial class InvoiceGetGridQuery
            {
                public GridFilterRequest? Grid { get; init; }
            }
            """);

        HasDiagnostic(result, "PRAG0532").Should().BeTrue(
            "a GET has no body, and the query string cannot carry a request object");
    }
}
