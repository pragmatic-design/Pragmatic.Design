using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     What a query declares as its default, and whether the wire agrees.
/// </summary>
/// <remarks>
///     <para>
///         A query's properties are <c>init</c>-only, so the generated endpoint sets them in an object
///         initializer — and an object initializer assigns unconditionally. An absent query-string
///         parameter arrives as <c>null</c> and was written straight over the declared default, so
///         <c>Outcome { get; init; } = Pending</c> meant "every outcome": the exact opposite of what it
///         says, silently, in every application.
///     </para>
///     <para>
///         An endpoint never had the problem — it emits <c>if (x is not null) endpoint.X = x;</c> and
///         the initializer survives on its own. The two shapes disagreed and only one of them was right.
///     </para>
///     <para>
///         Found in a consumer application, where a review queue listed the candidates it had already
///         closed, and only after three wrong diagnoses of the feature under test.
///     </para>
/// </remarks>
public class QueryDefaultValueTests : EndpointsGeneratorTestBase
{
    private static string Source(string properties) => $$"""
        using System;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query;
        using Pragmatic.Persistence.Query.Attributes;

        namespace TestApp.Review;

        public enum ReviewState { Pending = 0, Done = 1 }

        [Entity]
        public partial class Note : IEntity
        {
            public Guid PersistenceId { get; set; }
            public string Text { get; private set; } = "";
        }

        public sealed class NoteDto
        {
            public Guid Id { get; init; }
        }

        [Query<Note, NoteDto>]
        [Endpoint(HttpVerb.Get, "api/review/notes")]
        public partial class SearchNotesQuery
        {
        {{properties}}
        }
        """;

    private static string Generated(string properties)
        => GetGeneratedSourcesAsDictionary(RunGeneratorWithPersistence(Source(properties)))
            .First(kv => kv.Key.Contains("SearchNotesQuery.Endpoint")).Value;

    /// <summary>
    ///     An enum default survives, and arrives qualified: the author's using is not in scope here.
    /// </summary>
    [Fact]
    public void AnEnumDefault_SurvivesBinding()
    {
        Generated("""
                public ReviewState? State { get; init; } = ReviewState.Pending;
                public int Page { get; init; } = 1;
                public int PageSize { get; init; } = 50;
            """)
            .Should().Contain("State = state ?? global::TestApp.Review.ReviewState.Pending");
    }

    [Fact]
    public void AStringDefault_SurvivesBinding()
    {
        Generated("""
                public string? Sort { get; init; } = "newest";
                public int Page { get; init; } = 1;
                public int PageSize { get; init; } = 50;
            """)
            .Should().Contain("Sort = sort ?? \"newest\"");
    }

    /// <summary>
    ///     A property with no initializer keeps binding to null, which is what "no filter" means.
    /// </summary>
    [Fact]
    public void APropertyWithoutADefault_IsUnchanged()
    {
        var generated = Generated("""
                public string? Text { get; init; }
                public int Page { get; init; } = 1;
                public int PageSize { get; init; } = 50;
            """);

        generated.Should().Contain("Text = text");
        generated.Should().NotContain("Text = text ??");
    }

    /// <summary>
    ///     The declared page size is the one the endpoint uses.
    /// </summary>
    /// <remarks>
    ///     Paging keeps framework defaults for a query that declares none — a caller may omit both and
    ///     still expect a first page — but a number written in the source must not be answered with a
    ///     different one.
    /// </remarks>
    [Fact]
    public void TheDeclaredPageSize_IsTheOneUsed()
    {
        Generated("""
                public string? Text { get; init; }
                public int Page { get; init; } = 1;
                public int PageSize { get; init; } = 50;
            """)
            .Should().Contain("int pageSize = 50").And.NotContain("int pageSize = 20");
    }

    /// <summary>
    ///     And a query that declares nothing keeps the framework's own.
    /// </summary>
    [Fact]
    public void AQueryThatDeclaresNoPageSize_KeepsTheFrameworkDefault()
    {
        Generated("""
                public string? Text { get; init; }
                public int Page { get; init; }
                public int PageSize { get; init; }
            """)
            .Should().Contain("int pageSize = 20");
    }
}
