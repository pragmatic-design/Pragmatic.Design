using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Caching;

/// <summary>
///     A cache key must tell two values of a complex property apart.
/// </summary>
/// <remarks>
///     <para>
///         <c>Convert.ToString</c> on a class returns the <b>type name</b> — the same string for every
///         instance — so a complex property keyed that way contributes a constant to the key and every
///         value of it collapses onto one entry. A second caller sending a different filter is served
///         the first one's page, for as long as the entry lives.
///     </para>
///     <para>
///         Collections have the identical failure and are serialised element by element; a complex
///         object needs the same treatment.
///     </para>
///     <para>
///         ⚠️ A host per test hides this: a cold cache per test means no two requests ever share an
///         entry. It shows only when one host is shared across an application's tests.
///     </para>
/// </remarks>
public class TheKeyOfAComplexPropertyTests
{
    private static string CacheSourceFor(string declarations)
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            $$"""
            using Pragmatic.Caching.Attributes;

            namespace Catalogue;

            {{declarations}}
            """,
            GeneratorTestHelper.FromType<global::Pragmatic.Caching.Attributes.CacheableAttribute>());

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "SearchQuery.Cache");
        if (string.IsNullOrEmpty(generated))
        {
            var files = string.Join(", ", GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Keys);
            throw new Xunit.Sdk.XunitException($"No cache file was generated. Generated: [{files}]");
        }

        return generated!;
    }

    private const string Filter = """
        public sealed class LocationFilter
        {
            public string? City { get; set; }
            public string? Country { get; set; }
        }
        """;

    [Fact]
    public void ANestedScalar_IsPartOfTheKey()
    {
        var source = CacheSourceFor($$"""
            {{Filter}}

            [Cacheable(Duration = "5m")]
            public partial class SearchQuery
            {
                public int Page { get; set; }
                public LocationFilter? Location { get; set; }
            }
            """);

        source.Should().Contain("Location?.City",
            "two filters differing only in their city must not share a cache entry, and the only way "
            + "the key can tell them apart is by carrying the city");
        source.Should().Contain("Location?.Country",
            "every scalar the filter carries changes the answer, so every one of them is part of the key");
    }

    /// <summary>The control: an ordinary property is still keyed the way it always was.</summary>
    /// <remarks>
    ///     Without it, "walk into complex properties" and "rewrite every key" produce the same green on
    ///     the assertion above, and the second would change the key of every cached query in existence.
    /// </remarks>
    [Fact]
    public void AScalarProperty_IsUnchanged()
    {
        var source = CacheSourceFor($$"""
            {{Filter}}

            [Cacheable(Duration = "5m")]
            public partial class SearchQuery
            {
                public int Page { get; set; }
                public LocationFilter? Location { get; set; }
            }
            """);

        source.Should().Contain("Page={System.Uri.EscapeDataString(System.Convert.ToString(Page,",
            "a scalar was never the problem and its key fragment does not move");
    }

    /// <summary>
    ///     A filter that refers to itself is reported, not silently keyed on half of itself.
    /// </summary>
    /// <remarks>
    ///     The walk stops on a repeated type and on the fourth level. Whatever it read still
    ///     differentiates; what lies past the limit does not, so two requests differing only down there
    ///     would share an entry — the defect this whole change removes, one level deeper. PRAG1705 is
    ///     the difference between that and a key nobody was told about.
    /// </remarks>
    [Fact]
    public void AFilterThatRefersToItself_IsReported()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            """
            using Pragmatic.Caching.Attributes;

            namespace Catalogue;

            public sealed class NodeFilter
            {
                public string? Name { get; set; }
                public NodeFilter? Parent { get; set; }
            }

            [Cacheable(Duration = "5m")]
            public partial class SearchQuery
            {
                public NodeFilter? Node { get; set; }
            }
            """,
            GeneratorTestHelper.FromType<global::Pragmatic.Caching.Attributes.CacheableAttribute>());

        GeneratorTestHelper.HasDiagnostic(result, "PRAG1705").Should().BeTrue(
            "the key cannot read past the cycle, and a key that is blind past a point has to say so");
        GeneratorTestHelper.GetDiagnosticsById(result, "PRAG1705")
            .Single().GetMessage().Should().Contain("Node").And.Contain("SearchQuery",
                "the message names the property and the type, so the reader knows where to look");
    }

    /// <summary>The control: an ordinary filter is read to the bottom and reported as nothing.</summary>
    [Fact]
    public void AFilterThatEnds_IsNotReported()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            $$"""
            using Pragmatic.Caching.Attributes;

            namespace Catalogue;

            {{Filter}}

            [Cacheable(Duration = "5m")]
            public partial class SearchQuery
            {
                public LocationFilter? Location { get; set; }
            }
            """,
            GeneratorTestHelper.FromType<global::Pragmatic.Caching.Attributes.CacheableAttribute>());

        GeneratorTestHelper.HasDiagnostic(result, "PRAG1705").Should().BeFalse(
            "this one the walk reads to the bottom, and a diagnostic on every filter would be noise");
    }

    /// <summary>
    ///     The static <c>Create</c> helper takes the filter itself, and reads the same path from it.
    /// </summary>
    /// <remarks>
    ///     The two keys have to agree or the helper writes entries the query never reads. They are
    ///     built from one model for that reason, and this is the assertion that says so.
    /// </remarks>
    [Fact]
    public void TheStaticHelper_ReadsTheSamePath()
    {
        var source = CacheSourceFor($$"""
            {{Filter}}

            [Cacheable(Duration = "5m")]
            public partial class SearchQuery
            {
                public int Page { get; set; }
                public LocationFilter? Location { get; set; }
            }
            """);

        source.Should().Contain("location?.City",
            "the helper's parameter is the filter, so the path is the same one read from the argument");
    }
}
