using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A trait's child marked <c>[TraitOf&lt;TParent&gt;]</c> is visible on the Sql engine only where its parent is: the
///     parent's owner, read through the navigation to it.
/// </summary>
/// <remarks>
///     TraitFeature emits the mark on the children it generates (the trait snapshots show it); here the child is written by
///     hand, as the Sql engine's suites do while TraitFeature generates children for EF Core only. The mark was the spike
///     bridge's own attribute, which two bridges in one application would have declared twice.
/// </remarks>
public class TheSqlEngineReadsTheTraitChildsMarkTests
{
    private const string Source = """
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Sql;

        namespace Contoso.Reviews
        {
            [Entity]
            [HasOwner]
            public partial class Review
            {
                public string Text { get; set; } = "";
            }

            [Entity]
            /*MARK*/
            [Relation.ManyToOne<Review>]
            public partial class ReviewComment
            {
                public string Text { get; set; } = "";
            }

            public static class Reads
            {
                public static System.Threading.Tasks.ValueTask<System.Collections.Generic.List<ReviewComment>> All(SqlSession session)
                    => session.Query<ReviewComment>().ToListAsync();
            }
        }

        namespace Pragmatic.Persistence.Sql.Pragmatic
        {
            public static class PragmaticSqlFilters { }
        }
        """;

    [Fact]
    public void AMarkedChild_ReadsItsParentsOwner()
        => Interceptors(Source.Replace("/*MARK*/", "[TraitOf<Review>]"))
            .Should().Contain("OwnerId", "the comment is visible where its review is: the review's owner");

    /// <summary>The control: without the mark the child is an entity like any other, with no owner of its own.</summary>
    [Fact]
    public void AnUnmarkedChild_ReadsNoOwner()
        => Interceptors(Source.Replace("/*MARK*/", ""))
            .Should().NotContain("OwnerId", "nothing says whose child it is");

    /// <summary>The plan of the one chain, the read of the comments: the review's map names OwnerId whatever the comment is.</summary>
    private static string Interceptors(string source)
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, References);
        var text = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Single(s => s.Key == "_Infra.Sql.Interceptors.g.cs").Value;
        var start = text.IndexOf("file static class Plan_0", StringComparison.Ordinal);
        var end = text.IndexOf("file static class PlanRegistry", start, StringComparison.Ordinal);
        return text.Substring(start, end - start);
    }

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Sql.SqlSession>(),
        .. ByName("Pragmatic.Specification", "System.Linq.Queryable", "System.Linq.Expressions", "System.Text.Json", "System.ComponentModel.Annotations")
    ];

    private static IEnumerable<MetadataReference> ByName(params string[] names)
        => names.Select(n => GeneratorTestHelper.TryGetAssemblyReference(n)
            ?? throw new InvalidOperationException($"'{n}' does not resolve in the test process."));
}
