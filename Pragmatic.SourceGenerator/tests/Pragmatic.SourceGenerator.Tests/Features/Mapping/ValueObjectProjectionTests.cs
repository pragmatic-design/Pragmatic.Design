using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Mapping;

/// <summary>
///     A <c>[ValueObject]</c> property survives the projection, and what cannot survive says so.
/// </summary>
/// <remarks>
///     <para>
///         The projection keeps what it can translate to SQL. A value object is mapped as an EF Core
///         complex type and EF projects it whole — <c>new Dto { Position = e.Position }</c> selects
///         <c>Position_Start</c> and <c>Position_Length</c> — but it was not on the list, so it was
///         dropped from the expression without a word. The columns held the right numbers while every
///         projected read answered with the DTO's own initialiser.
///     </para>
///     <para>
///         Found by building it in an application, not by reading the generator: the framework's own
///         E2E for value objects reads the entity rather than a projection, and asserts the type's
///         default values.
///     </para>
/// </remarks>
public class ValueObjectProjectionTests
{
    [Fact]
    public void AValueObjectProperty_IsCarriedByTheProjection()
    {
        var result = Run("""
            [ValueObject]
            public partial record TextSpan
            {
                public TextSpan(int start, int length) { Start = start; Length = length; }
                public int Start { get; init; }
                public int Length { get; init; }
                private static TextSpan Validate(int start, int length) => new(start, length);
            }

            [Entity]
            [BelongsTo<ThingBoundary>]
            public partial class Mention : IEntity
            {
                public string Surface { get; private set; } = "";
                public TextSpan Position { get; private set; } = new(0, 0);
            }

            [MapFrom<Mention>]
            [GenerateProjection]
            public partial class MentionDto
            {
                public string Surface { get; init; } = "";
                public TextSpan Position { get; init; } = new(0, 0);
            }
            """);

        var mapping = GeneratorTestHelper.GetGeneratedSource(result, "MentionDto.Mapping");

        mapping.Should().Contain("Position = entity.Position",
            "EF projects a complex type whole, so leaving it out returns the DTO's initialiser "
            + "while the columns hold the value");
        mapping.Should().Contain("Surface = entity.Surface",
            "the control: the rest of the projection is unchanged");
    }

    /// <summary>
    ///     And it survives the projection that inlines a child collection, which is the path a read
    ///     of the aggregate actually takes.
    /// </summary>
    /// <remarks>
    ///     Two filters, not one. The top-level projection keeps what is SQL-translatable; the inlined
    ///     one keeps what is a simple type, a nested DTO or a collection of them — a value object was
    ///     none of the three, so it fell through and was omitted <b>without setting the dropped
    ///     flag</b>, which is why not even PRAG0326 said anything. Fixing only the first filter left
    ///     the endpoint that reads a term with its mentions still answering with zeroes.
    /// </remarks>
    [Fact]
    public void AValueObjectInsideAChildCollection_SurvivesTheInlinedProjection()
    {
        var result = Run("""
            [ValueObject]
            public partial record TextSpan
            {
                public TextSpan(int start, int length) { Start = start; Length = length; }
                public int Start { get; init; }
                public int Length { get; init; }
                private static TextSpan Validate(int start, int length) => new(start, length);
            }

            [Entity]
            [BelongsTo<ThingBoundary>]
            [Relation.OneToMany<Sighting>]
            public partial class Word : IEntity
            {
                public string Text { get; private set; } = "";
            }

            [Entity]
            [BelongsTo<ThingBoundary>]
            [PartOf<Word>]
            public partial class Sighting : IEntity
            {
                public TextSpan Position { get; private set; } = new(0, 0);
            }

            [MapFrom<Sighting>]
            [GenerateProjection]
            public partial class SightingDto
            {
                public TextSpan Position { get; init; } = new(0, 0);
            }

            [MapFrom<Word>]
            [GenerateProjection]
            public partial class WordDto
            {
                public string Text { get; init; } = "";
                public List<SightingDto> Sightings { get; init; } = [];
            }
            """);

        GeneratorTestHelper.GetGeneratedSource(result, "WordDto.Mapping")
            .Should().Contain("Position = x.Position",
                "the inlined element projection has its own filter, and a value object fell through "
                + "all of its branches");
    }

    /// <summary>
    ///     And a property the projection genuinely cannot carry is reported.
    /// </summary>
    /// <remarks>
    ///     PRAG0340. The omission is sometimes the only thing the generator can do — a converter read
    ///     through a navigation that may be null cannot be computed after the read (over a column it
    ///     is) — but the author has to know which properties survive a query and which need
    ///     <c>FromEntity</c>. Silence was the previous answer and it is the worst one: a default in the
    ///     shape of a real value.
    /// </remarks>
    [Fact]
    public void APropertyTheProjectionCannotCarry_IsReported()
    {
        var result = Run("""
            public sealed class Shouty : Pragmatic.Mapping.IValueConverter<string, string>
            {
                public string Convert(string source) => source.ToUpperInvariant();
                public string ConvertBack(string target) => target.ToLowerInvariant();
            }

            public sealed class Signature
            {
                public string Text { get; set; } = "";
            }

            [Entity]
            [BelongsTo<ThingBoundary>]
            public partial class Note : IEntity
            {
                public string Body { get; private set; } = "";
                public Signature? Signature { get; private set; }
            }

            [MapFrom<Note>]
            [GenerateProjection]
            public partial class NoteDto
            {
                public string Body { get; init; } = "";

                [MapProperty("Signature.Text")]
                [MapConverter<Shouty>]
                public string SignatureText { get; init; } = "";
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0340").Should().BeTrue(
            "a converter through a navigation that may be null cannot run after the read, so the projection "
            + "leaves the property at its default");
    }

    /// <summary>The control: an ordinary DTO reports nothing.</summary>
    /// <remarks>
    ///     Without it the test above would also pass on a diagnostic that fires for every property.
    /// </remarks>
    [Fact]
    public void ADtoTheProjectionCarriesWhole_ReportsNothing()
    {
        var result = Run("""
            [Entity]
            [BelongsTo<ThingBoundary>]
            public partial class Plain : IEntity
            {
                public string Name { get; private set; } = "";
                public int Count { get; private set; }
            }

            [MapFrom<Plain>]
            [GenerateProjection]
            public partial class PlainDto
            {
                public string Name { get; init; } = "";
                public int Count { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0340").Should().BeFalse();
    }

    private static SourceGenRunResult Run(string declarations)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            $$"""
            using System;
            using System.Collections.Generic;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Mapping.Attributes;
            using Pragmatic.Persistence.Query.Attributes;

            namespace Projections;

            public sealed class ThingBoundary;

            {{declarations}}
            """,
            GeneratorTestHelper.FromType<EntityAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Mapping.Attributes.MapFromAttribute<object>>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(global::Pragmatic.Endpoints.Processors.IEndpointPreProcessor)));
}
