using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A query reusing a rule that was named once, instead of restating it.
/// </summary>
/// <remarks>
///     <para>
///         The duplication this exists for is not the trivial filter — <c>[Filter] Guid? GuestId</c> is
///         shorter and clearer than any specification of the same predicate. It is the <em>rule</em>: a
///         sentence with meaning, like "confirmed and of this kind", which a query had to spell out
///         again as filter properties. Spelling it out is what made it forgettable, and what let one
///         query expose a switch to turn half of it off.
///     </para>
///     <para>
///         Recognised by type: a property of type <c>Specification&lt;TEntity&gt;</c> is applied whole.
///         The input it reads carries <c>[BindSpecification]</c>, which says it is an input rather than
///         a filter — otherwise a nullable one would generate a <c>Where</c> <em>and</em> feed the
///         specification, applying the same rule twice through two mechanisms.
///     </para>
/// </remarks>
public class QuerySpecificationTests
{
    [Fact]
    public void ASpecificationProperty_IsAppliedInApply()
    {
        var generated = Run("""
            [Query<Thing, Thing>]
            public partial class RuledQuery
            {
                [BindSpecification]
                public bool? OnlyNamed { get; init; }

                public Specification<Thing>? Rule
                    => OnlyNamed == true ? Spec<Thing>.Where(t => t.Name != "") : null;
            }
            """);

        generated.Should().Contain("SpecificationExtensions.Where(query, this.Rule)",
            "a specification property is a contribution of its own, applied whole");
    }

    /// <summary>
    ///     A rule that reads no input is static, as the analyzers ask (CA1822), and applied all
    ///     the same. Skipped because it was static, it vanished from the query without a word.
    /// </summary>
    [Fact]
    public void AStaticSpecificationProperty_IsAppliedToo()
    {
        var generated = Run("""
            [Query<Thing, Thing>]
            public partial class RuledQuery
            {
                public static Specification<Thing> Rule => Spec<Thing>.Where(t => t.Name != "");
            }
            """);

        generated.Should().Contain("SpecificationExtensions.Where(query, RuledQuery.Rule)");
        generated.Should().Contain("spec = spec & RuledQuery.Rule");
    }

    /// <remarks>
    ///     The two have to agree. Left out of <c>ToSpecification()</c>, the same query would answer one
    ///     thing through the runner and another through the repository, and only one of them would be
    ///     the query the author wrote.
    /// </remarks>
    [Fact]
    public void ASpecificationProperty_IsAlsoComposedInToSpecification()
    {
        var generated = Run("""
            [Query<Thing, Thing>]
            public partial class RuledQuery
            {
                [BindSpecification]
                public bool? OnlyNamed { get; init; }

                public Specification<Thing>? Rule
                    => OnlyNamed == true ? Spec<Thing>.Where(t => t.Name != "") : null;
            }
            """);

        generated.Should().Contain("spec = spec & this.Rule",
            "Apply and ToSpecification must describe the same query");
    }

    /// <remarks>
    ///     Without the marker this input is a plain non-nullable scalar that names no column — which is
    ///     PRAG0707's definition of a filter somebody forgot. With it, the value is declared as an input.
    /// </remarks>
    [Fact]
    public void AMarkedInput_IsNotReportedAsAForgottenFilter()
    {
        var result = RunRaw("""
            [Query<Thing, Thing>]
            public partial class RuledQuery
            {
                [BindSpecification]
                public bool OnlyNamed { get; init; }

                public Specification<Thing>? Rule
                    => OnlyNamed ? Spec<Thing>.Where(t => t.Name != "") : null;
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0707").Should().BeFalse();
    }

    /// <remarks>
    ///     The attribute is a claim. Mark an input, then never write the specification, and the value is
    ///     read and dropped again — with a declaration standing over it saying otherwise, which is
    ///     quieter than the silence the attribute exists to lift.
    /// </remarks>
    [Fact]
    public void PRAG0709_AMarkedInputWithNoSpecification_IsReported()
    {
        var result = RunRaw("""
            [Query<Thing, Thing>]
            public partial class RuledQuery
            {
                [BindSpecification]
                public bool OnlyNamed { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0709").Should().BeTrue(
            "the query claims a specification reads the input and declares none");
    }

    [Fact]
    public void PRAG0709_StaysSilentWhenTheSpecificationIsThere()
    {
        var result = RunRaw("""
            [Query<Thing, Thing>]
            public partial class RuledQuery
            {
                [BindSpecification]
                public bool OnlyNamed { get; init; }

                public Specification<Thing>? Rule
                    => OnlyNamed ? Spec<Thing>.Where(t => t.Name != "") : null;
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0709").Should().BeFalse();
    }

    /// <remarks>
    ///     Found by the silent-drops ratchet while it was refusing a change of mine: the transform
    ///     answered <c>null</c> for a non-partial query, which the pipeline cannot tell from "not my
    ///     node", so the attribute produced no Apply, no projection and no endpoint without a word.
    ///     Actions have said this since PRAG0400.
    /// </remarks>
    [Fact]
    public void PRAG0712_AQueryThatIsNotPartial_IsSkippedWithoutAGeneratorCopy()
    {
        var result = RunRaw("""
            [Query<Thing, Thing>]
            public class NotPartialQuery
            {
                public string? Name { get; init; }
            }
            """);

        // PRAG0712 is the companion analyzer's, on the declaration
        // (AMustBePartialDiagnosticHasOneOwnerTests); the generator writes nothing into the type.
        GeneratorTestHelper.HasDiagnostic(result, "PRAG0712").Should().BeFalse();
    }

    [Fact]
    public void PRAG0712_StaysSilentOnAPartialQuery()
    {
        var result = RunRaw("""
            [Query<Thing, Thing>]
            public partial class PartialQuery
            {
                public string? Name { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0712").Should().BeFalse();
    }

    private static string Run(string declarations)
        => string.Join("\n", GeneratorTestHelper.GetGeneratedSourcesAsDictionary(RunRaw(declarations)).Values);

    private static SourceGenRunResult RunRaw(string declarations)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            $$"""
            using System;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query.Attributes;
            using Pragmatic.Specification;

            namespace Ruled;

            public sealed class ThingBoundary;

            [Entity]
            [BelongsTo<ThingBoundary>]
            public partial class Thing : IEntity
            {
                public string Name { get; private set; } = "";
            }

            {{declarations}}
            """,
            GeneratorTestHelper.FromType<EntityAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(global::Pragmatic.Specification.Specification<>)));
}
