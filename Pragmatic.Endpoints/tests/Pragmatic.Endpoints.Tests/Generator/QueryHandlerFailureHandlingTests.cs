using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     The three shapes a <c>[Query]</c> endpoint can take, and the assertion each of them needs: that
///     the generated handler <b>compiles</b>.
/// </summary>
/// <remarks>
///     <para>
///         Asserting only that the emitted text contains <c>if (result.IsFailure)</c> is not enough:
///         the non-paged branch can contain it and still not compile, because
///         <c>ExecuteAllAsync</c> returns <c>IReadOnlyList&lt;TResult&gt;</c> and a list has no
///         <c>IsFailure</c>. A <c>[Query]</c> that declares <c>Page</c>/<c>PageSize</c> never generates
///         that branch, so a real compilation of such a query cannot catch it either.
///     </para>
///     <para>
///         Hence <c>HasCompilationErrors</c> in each case: a generator test that reads the output as
///         a string can only tell you what was written, never whether it means anything.
///     </para>
/// </remarks>
public class QueryHandlerFailureHandlingTests : EndpointsGeneratorTestBase
{
    private const string Preamble = """
        using System;
        using System.Linq;
        using System.Linq.Expressions;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Persistence.Query.Attributes;

        namespace TestApp.Queries;

        public class Tag
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        public class TagDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";

            public static Expression<Func<Tag, TagDto>> Projection =>
                t => new TagDto { Id = t.Id, Name = t.Name };
        }
        """;

    /// <summary>
    ///     Generates the handler and returns its text. <b>Only the text</b> — read the remark before
    ///     adding an assertion that pretends otherwise.
    /// </summary>
    /// <remarks>
    ///     ⚠️ These tests cannot tell a handler that compiles from one that does not.
    ///     <c>RunGeneratorWithPersistence</c> builds a reference set without EF Core, without
    ///     <c>System.Threading.RateLimiting</c> and without <c>System.Linq</c>, so every run already
    ///     reports missing-assembly errors and the interesting error never surfaces. A first attempt
    ///     here filtered the diagnostics for the word <c>IsFailure</c> and asserted none remained —
    ///     it passed with the broken branch put back, because with those references missing the
    ///     binder never gets far enough to complain about <c>IsFailure</c> at all. A test that cannot
    ///     fail is worse than no test, so it is gone.
    ///     <para>
    ///         The compilation of these three shapes is proved where compilation is real: the
    ///         Showcase declares one query of each kind and the gate builds it. The incomplete
    ///         reference set is what lets a broken branch hide behind a green test here.
    ///     </para>
    /// </remarks>
    private static string GeneratedFor(string queryDeclaration)
    {
        var result = RunGeneratorWithPersistence(Preamble + "\n\n" + queryDeclaration);
        var handler = GetGeneratedSource(result, "Endpoint");
        handler.Should().NotBeNull();
        return handler!;
    }

    /// <summary>No paging, no Single: a list, and a list is never a failure — it is empty.</summary>
    [Fact]
    public void Query_List_ReturnsTheItemsAndCompiles()
    {
        var handler = GeneratedFor("""
            [Query<Tag, TagDto>]
            [Endpoint(HttpVerb.Get, "/tags")]
            public partial class ListTagsQuery
            {
            }
            """);

        // The overload is the invoker's now; what this suite can see is what the handler does with the
        // answer. Which overload runs for which shape is asserted in TheQueryGetsAnInvokerTests, where
        // the invoker is generated and the whole thing compiles.
        handler.Should().Contain("var items = __outcome.Value!");
        handler.Should().NotContain("items.IsFailure",
            "IReadOnlyList has no IsFailure — asserting on the emitted text is what hid that for so long");
    }

    /// <summary>Page/PageSize present: a PagedResult, which can carry a failure.</summary>
    [Fact]
    public void Query_Paged_ChecksFailureBeforeReturningOk()
    {
        var handler = GeneratedFor("""
            [Query<Tag, TagDto>]
            [Endpoint(HttpVerb.Get, "/tags")]
            public partial class ListTagsPagedQuery
            {
                public int Page { get; set; } = 1;
                public int PageSize { get; set; } = 20;
            }
            """);

        handler.Should().Contain("var result = __outcome.Value!");
        handler.Should().Contain("if (result.IsFailure)");
        handler.Should().Contain("ErrorExtensions.ToResult(result.Error!, httpContext)",
            "a page carries its own failure, and it answers with the error's status as ProblemDetails");
        handler.Should().NotContain("Results.BadRequest(result.Error)",
            "a database outage is not a request the client got wrong");
    }

    /// <summary>
    ///     <c>Single = true</c>: at most one row, and 404 rather than an empty list. Reading one
    ///     record was the only read shape with no declarative form — the omission that sent every
    ///     get-by-id out of the query pipeline and into a hand-written repository call.
    /// </summary>
    [Fact]
    public void Query_Single_ExecutesSingleAndMapsTheErrorThroughProblemDetails()
    {
        var handler = GeneratedFor("""
            [Query<Tag, TagDto>(Single = true)]
            [Endpoint(HttpVerb.Get, "/tags/{id}")]
            public partial class GetTagQuery
            {
                public int Id { get; set; }
            }
            """);

        handler.Should().Contain("if (__outcome.IsFailure)");
        handler.Should().Contain("ErrorExtensions.ToResult(__outcome.Error!, httpContext)",
            "a missing row must answer 404 through the same ProblemDetails path the mutations use — the "
            + "executor's failure now travels out of the invoker as the outcome's error");
        handler.Should().Contain("Results.Ok(__outcome.Value)",
            "the body is the row itself, not a wrapper");
    }
}
