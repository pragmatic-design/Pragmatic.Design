using System;
using System.Linq;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     One declaration, two generated members, and both have to hold.
/// </summary>
/// <remarks>
///     <para>
///         <c>[SearchAcross]</c> has to be rendered for <c>Apply</c> and for <c>ToSpecification</c>,
///         emitted just below by the same template. Rendered for <c>Apply</c> alone,
///         <c>ToSpecification</c> writes the filter's own property looked up on the entity —
///         <c>e.Search.Contains(…)</c>, a <c>CS1061</c> — so the type does not compile and
///         <b>neither</b> half ever runs. No declaration avoids it: the template emits
///         <c>ToSpecification</c> as soon as a property is filterable, and <c>[SearchAcross]</c> makes
///         it filterable.
///     </para>
///     <para>
///         ⚠️ A template test that asserts over the whole rendered text is satisfied by a correct
///         <c>Apply</c> half while the file cannot be built. The working half is the one an example
///         shows.
///     </para>
/// </remarks>
public class SearchAcrossOnBothMembersTests
{
    /// <summary>The whole generated filter compiles — which is the assertion the text cannot make.</summary>
    [Fact]
    public void AFilterDeclaringSearchAcross_Compiles()
    {
        var result = Run();

        ErrorsInTheGridFilter(result).Should().BeEmpty(
            "both generated members are part of the same type");
    }

    /// <summary>And the specification searches across the same properties as the query.</summary>
    /// <remarks>
    ///     Compiling is not enough on its own: a <c>ToSpecification</c> that dropped the property
    ///     would compile and answer every question with "no condition", which is a filter that
    ///     matches everything.
    /// </remarks>
    [Fact]
    public void TheSpecification_SearchesTheSamePropertiesAsTheQuery()
    {
        var specification = TheSpecificationOf(Run());

        specification.Should().Contain("e.Word.Contains");
        specification.Should().Contain("e.Definition.Contains");
        specification.Should().Contain("||", "one value against several columns is an OR");
        specification.Should().NotContain("e.Search",
            "Search is the filter's own property, and the entity has no such member");
    }

    /// <summary>
    ///     The control: an ordinary filterable property still reads its own path in the specification.
    /// </summary>
    /// <remarks>
    ///     Without it, "the specification does not name Search" is satisfied by a specification that
    ///     names nothing at all — which compiles, and matches every row.
    /// </remarks>
    [Fact]
    public void AnOrdinaryFilterableProperty_IsUnchangedInTheSpecification()
    {
        var specification = TheSpecificationOf(Run());

        specification.Should().Contain("e.Kind ==",
            "a plain filterable property is compared to its own column");
    }

    /// <summary>Errors reported inside the generated grid filter, and nowhere else.</summary>
    /// <remarks>
    ///     The whole compilation cannot be the subject: this test project references a deliberate
    ///     subset, so the repository and setter files report missing assemblies that have nothing to
    ///     do with the filter. Asserting over all of them measures the reference list.
    /// </remarks>
    private static System.Collections.Generic.IReadOnlyList<string> ErrorsInTheGridFilter(
        SourceGenRunResult result)
        => GeneratorTestHelper.GetCompilationErrors(result)
            .Where(d => d.Location.SourceTree?.FilePath.Contains("TermGrid") == true)
            .Select(d => d.ToString())
            .ToList();

    private static string TheSpecificationOf(SourceGenRunResult result)
    {
        var generated = GeneratorTestHelper.GetGeneratedSource(result, "TermGrid");

        generated.Should().NotBeNull("the grid filter is generated at all");

        var start = generated!.IndexOf("ToSpecification", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "the template emits it as soon as a property is filterable");

        return generated[start..];
    }

    private static SourceGenRunResult Run()
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query.Attributes;

            namespace SearchApp;

            public sealed class KnowledgeBoundary;

            [Entity]
            [BelongsTo<KnowledgeBoundary>]
            public partial class Glossary : IEntity
            {
                public string Word { get; private set; } = "";
                public string Definition { get; private set; } = "";
                public int Kind { get; private set; }
            }

            [GridFilter<Glossary>]
            public partial class TermGrid
            {
                [SearchAcross(nameof(Glossary.Word), nameof(Glossary.Definition))]
                public string? Search { get; set; }

                [Filterable]
                public int? Kind { get; set; }
            }
            """,
            GeneratorTestHelper.FromType<EntityAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Specification.Specification<object>>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(global::Pragmatic.Result.Result<,>)),
            GeneratorTestHelper.FromType<global::Pragmatic.Result.IError>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(global::Pragmatic.Ensure.Ensure)),
            GeneratorTestHelper.FromType<global::Pragmatic.Mapping.Attributes.MapFromAttribute<object>>(),
            GeneratorTestHelper.FromType<global::Microsoft.EntityFrameworkCore.DbContext>(),
            GeneratorTestHelper.TryGetAssemblyReference("System.Linq.Queryable")
                ?? throw new InvalidOperationException("System.Linq.Queryable does not resolve"),
            GeneratorTestHelper.TryGetAssemblyReference("Microsoft.EntityFrameworkCore.Abstractions")
                ?? throw new InvalidOperationException("EF Core abstractions do not resolve"),
            GeneratorTestHelper.TryGetAssemblyReference("Microsoft.Extensions.DependencyInjection.Abstractions")
                ?? throw new InvalidOperationException("DI abstractions do not resolve"),
            GeneratorTestHelper.TryGetAssemblyReference("System.Text.Json")
                ?? throw new InvalidOperationException("System.Text.Json does not resolve"),
            GeneratorTestHelper.TryGetAssemblyReference("System.ComponentModel.Annotations")
                ?? throw new InvalidOperationException("Annotations do not resolve"));
}
