using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     A query validates its inputs before it runs.
/// </summary>
/// <remarks>
///     <para>
///         <c>ValidationFilter</c> sits at Order 100 in the action-filter chain, and that chain is
///         executed by an invoker, which a query endpoint does not go through. With the binding as the
///         only check on the inputs, a value that parses would be accepted however absurd it was — a
///         page of <c>-5</c>, a size of a million — and the query would run on it.
///     </para>
///     <para>
///         The SG generates a validator for any type carrying validation attributes, queries included;
///         what it needs is a caller.
///     </para>
///     <para>
///         ⚠️ The check is in the query's generated invoker, not in the handler, and both doors go
///         through it — so the same input is refused whether it arrives over HTTP or from a caller in
///         the same process. It is a run-time <c>is ISyncValidator</c> test, because the validator is
///         a partial this generator emits, so asking the compilation whether the query implements it
///         would answer "no" for every query that has one.
///     </para>
///     <para>
///         What that leaves here is the half this suite can see: the handler asks the invoker, and it
///         answers the refusal before it answers rows. That the refusal happens before the read is
///         measured where it happens, by <c>QueryInvokerTests</c> in Pragmatic.Actions, which runs the
///         pipeline instead of reading it.
///     </para>
/// </remarks>
public class QueryValidationTests : EndpointsGeneratorTestBase
{
    private const string Source = """
        using System;
        // Pragmatic's own [Range], not the DataAnnotations one. PRAG0210 exists because the latter
        // generates no check at all — and this test was written with it, stayed green, and proved
        // nothing: the `is ISyncValidator` cast it asserts on is emitted whether a validator exists or
        // not. A consumer application caught it; the test could not.
        using Pragmatic.Validation.Attributes;
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
            [Range(1, int.MaxValue)]
            public int Page { get; init; } = 1;

            [Range(1, 200)]
            public int PageSize { get; init; } = 20;
        }
        """;

    [Fact]
    public void TheHandlerAsksTheInvoker_WhichValidates()
    {
        var generated = GetGeneratedSource(RunGeneratorWithPersistence(Source), "SearchInvoices.Endpoint");

        generated.Should().Contain(".Invoker(httpContext.RequestServices)",
            "the route runs the query the one way it can be run");
        generated.Should().NotContain("ISyncValidator",
            "a second copy of the validation beside the invoker's is how two doors come to disagree");
    }

    /// <summary>
    ///     And the query really has one, so the cast finds something.
    /// </summary>
    /// <remarks>
    ///     Without this the test above passes on a query with no validator at all — the cast is emitted
    ///     unconditionally. That is exactly what happened while the source used the DataAnnotations
    ///     <c>[Range]</c>, which <c>PRAG0210</c> reports as generating no check.
    /// </remarks>
    [Fact]
    public void TheQueryActuallyImplementsTheValidator()
    {
        var validator = GetGeneratedSource(RunGeneratorWithPersistence(Source), "SearchInvoices.Validator");

        validator.Should().NotBeNull("declared bounds are what produce a validator")
            .And.Contain("ISyncValidator");
    }

    /// <summary>The refusal is answered, and answered before any rows are.</summary>
    /// <remarks>
    ///     <c>ValidationError</c> is an <c>IHttpError</c>, so the status stays 400 — the same answer this
    ///     route gave when it validated the input itself.
    /// </remarks>
    [Fact]
    public void AFailedValidationAnswersBeforeTheQueryRuns()
    {
        var generated = GetGeneratedSource(RunGeneratorWithPersistence(Source), "SearchInvoices.Endpoint");

        generated.Should().Contain("__outcome.IsFailure",
            "a refused input must not be answered with rows");

        var refusalIndex = generated!.IndexOf("__outcome.IsFailure", StringComparison.Ordinal);

        // The rows are answered through the serializer or through the type's generated writer.
        var okIndex = new[]
            {
                generated.IndexOf("Results.Ok", StringComparison.Ordinal),
                generated.IndexOf("GeneratedJsonResponse<", StringComparison.Ordinal),
            }
            .Where(i => i >= 0)
            .DefaultIfEmpty(-1)
            .Min();

        okIndex.Should().BeGreaterThan(-1, "the handler answers the rows somewhere");
        refusalIndex.Should().BeLessThan(okIndex,
            "answering the rows and then the error is not refusing");
    }
}
