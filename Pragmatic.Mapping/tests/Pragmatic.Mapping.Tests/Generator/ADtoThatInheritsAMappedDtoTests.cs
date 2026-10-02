using System.Linq;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     A <c>[MapFrom]</c> DTO that inherits another one, which is the shape
///     <c>[MapDerived]</c> requires.
/// </summary>
/// <remarks>
///     <para>
///         The inheritance is not optional: <c>PRAG0330</c> refuses a <c>[MapDerived]</c> whose derived
///         DTO does not inherit the base, and the attribute's own <c>&lt;example&gt;</c> shows
///         <c>DogDto : AnimalDto</c>. So every one of these declarations depends on both halves below,
///         and either alone would stop the build.
///     </para>
///     <para>
///         ⚠️ <b>Half one.</b> The template writes four <c>static</c> properties — <c>Selector</c>,
///         <c>Projection</c>, <c>RequiredNavigations</c>, <c>WrittenNavigations</c> — so when the DTO's
///         base is itself a mapping DTO, each has to carry <c>new</c>. <c>CS0108</c> is a warning by
///         default, which is why the runtime dispatch tests beside this file cannot see it; in this
///         repository's <c>--warnaserror</c> build it is an error, and the documented shape would not
///         compile at all.
///     </para>
///     <para>
///         ⚠️ <b>Half two.</b> Resolving a DTO member's source has to walk the derived entity's bases
///         too: reading only its own properties reports a member the <b>base entity</b> declares as
///         missing — PRAG0302 for a flattened path, PRAG0303 for a plain one.
///     </para>
/// </remarks>
public class ADtoThatInheritsAMappedDtoTests : MappingGeneratorTestBase
{
    private const string Zoo = """
        using Pragmatic.Mapping.Attributes;
        namespace Zoo
        {
            public class Animal { public string Name { get; set; } = ""; }
            public class Dog : Animal { public string Breed { get; set; } = ""; }

            [MapFrom<Animal>]
            [MapDerived<Dog, DogDto>]
            public partial class AnimalDto { public string Name { get; init; } = ""; }

            [MapFrom<Dog>]
            public partial class DogDto : AnimalDto { public string Breed { get; init; } = ""; }
        }
        """;

    // ── Half one: the statics hide their base's, and say so ────────────────────────────────────

    [Fact]
    public void TheDerivedDtosStatics_HideTheBasesDeliberately()
    {
        var result = RunGenerator(Zoo);

        var hiding = GetCompilationWarnings(result).Where(d => d.Id == "CS0108").ToList();

        hiding.Should().BeEmpty(
            "CS0108 is an error under --warnaserror, so the shape [MapDerived] requires did not build: "
            + string.Join("\n", hiding.Select(d => d.ToString())));
    }

    /// <summary>
    ///     Each static property the template writes says it hides its base's.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>new</c> goes before the <b>type</b>, not the name — <c>public static new Func&lt;…&gt;
    ///     Selector</c>. And it goes on the properties only: <c>FromEntity</c> is a static
    ///     <b>method</b> whose parameter type differs from the base's, so it overloads rather than
    ///     hides, and <c>new</c> there would be <c>CS0109</c>.
    /// </remarks>
    [Fact]
    public void EveryStaticPropertyTheTemplateWrites_CarriesNew()
    {
        var source = GetGeneratedSource(RunGenerator(Zoo), "DogDto.Mapping") ?? "";

        source.Should().Contain("static new Func<", "Selector hides the base's");
        source.Should().Contain("static new IReadOnlyList<string> RequiredNavigations");
        source.Should().Contain("static DogDto FromEntity",
            "a method overloads and must NOT carry new: CS0109 is an error here too");
    }

    /// <summary>
    ///     <c>Projection</c> and <c>WrittenNavigations</c> are the other two, and they only exist when
    ///     the declaration asks for them.
    /// </summary>
    /// <remarks>
    ///     Asserted on a separate declaration rather than by adding attributes to <c>Zoo</c>: the
    ///     dispatch cases beside this file read that source, and <c>[GenerateProjection]</c> on a
    ///     <c>[MapDerived]</c> base also raises <c>PRAG0331</c>, which is information about the
    ///     projection and not about hiding.
    /// </remarks>
    [Fact]
    public void TheProjectionAndTheWriteListCarryItToo()
    {
        var source = GetGeneratedSource(RunGenerator("""
            using Pragmatic.Mapping.Attributes;
            namespace Shapes
            {
                public class Animal { public string Name { get; set; } = ""; }
                public class Dog : Animal { public string Breed { get; set; } = ""; }

                [MapFrom<Animal>]
                [MapTo<Animal>]
                [GenerateProjection]
                public partial class AnimalDto { public string Name { get; init; } = ""; }

                [MapFrom<Dog>]
                [MapTo<Dog>]
                [GenerateProjection]
                public partial class DogDto : AnimalDto { public string Breed { get; init; } = ""; }
            }
            """), "DogDto.Mapping") ?? "";

        source.Should().Contain("static new Expression<", "Projection hides the base's");
        source.Should().Contain("static new IReadOnlyList<string> WrittenNavigations");
    }

    /// <summary>
    ///     The control: a DTO with no mapped base still writes its statics plainly.
    /// </summary>
    /// <remarks>
    ///     Without it, "the statics carry new" is satisfied by emitting <c>new</c> on every DTO in the
    ///     repository — which compiles, and would be a warning (CS0109) on the 160-odd that have no
    ///     base to hide.
    /// </remarks>
    [Fact]
    public void ADtoWithNoMappedBase_WritesItsStaticsWithoutNew()
    {
        var result = RunGenerator(Zoo);
        var source = GetGeneratedSource(result, "AnimalDto.Mapping") ?? "";

        source.Should().Contain("static Func<", "the base DTO still has its Selector");
        source.Should().NotContain("new Selector", "it hides nothing");
        GetCompilationWarnings(result).Should().NotContain(d => d.Id == "CS0109",
            "CS0109 is 'does not hide an inherited member' — the mirror mistake");
    }

    // ── Half two: a member the base ENTITY declares ─────────────────────────────────────────────

    /// <summary>
    ///     The base entity's members are <b>generated</b>, which is the half a hand-declared property
    ///     cannot reproduce.
    /// </summary>
    /// <remarks>
    ///     <c>[Relation.ManyToOne&lt;Invoice&gt;]</c> on <c>Fee</c> gives <c>Fee</c> its <c>Invoice</c>
    ///     navigation and its <c>InvoiceId</c> foreign key, and neither is on the symbol while this
    ///     generator runs — they are asked of <c>TraitPropertyResolver</c>, which read the attributes
    ///     of <b>that type</b> and not of its bases. A property declared by hand on the base resolves
    ///     through <c>PropertyAnalyzer.GetAllProperties</c>, which has always walked them, so the
    ///     defect only appears where the member is generated on a base.
    /// </remarks>
    private const string Fees = """
        using System;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;
        namespace Billing
        {
            [Entity]
            public partial class Invoice : IEntity { public string Number { get; set; } = ""; }

            [Entity]
            [Relation.ManyToOne<Invoice>]
            public partial class Fee : IEntity { }

            [Entity]
            public partial class ServiceFee : Fee { public string ServiceName { get; set; } = ""; }

            [MapFrom<Fee>]
            [MapDerived<ServiceFee, ServiceFeeDto>]
            public partial class FeeDto
            {
                public Guid InvoiceId { get; init; }
                public string InvoiceNumber { get; init; } = "";
            }

            [MapFrom<ServiceFee>]
            public partial class ServiceFeeDto : FeeDto { public string ServiceName { get; init; } = ""; }
        }
        """;

    [Fact]
    public void AMemberTheBaseEntityDeclares_ResolvesOnTheDerivedOne()
    {
        var result = RunGenerator(Fees);

        HasDiagnostic(result, "PRAG0302").Should().BeFalse(
            "InvoiceNumber flattens through Fee.Invoice, and ServiceFee inherits it: "
            + string.Join("\n", GetDiagnosticsById(result, "PRAG0302").Select(d => d.GetMessage())));

        HasDiagnostic(result, "PRAG0303").Should().BeFalse(
            "InvoiceId is declared on Fee, and a derived entity has everything its base has: "
            + string.Join("\n", GetDiagnosticsById(result, "PRAG0303").Select(d => d.GetMessage())));
    }

    /// <summary>
    ///     The control: a member no type in the chain declares is still reported.
    /// </summary>
    /// <remarks>
    ///     Without it, "the inherited member resolves" is satisfied by a walk that stopped reporting.
    /// </remarks>
    [Fact]
    public void AMemberNoTypeInTheChainDeclares_IsStillReported()
    {
        var result = RunGenerator(Fees.Replace(
            "public string ServiceName { get; init; } = \"\"; }",
            "public string NobodyHasThis { get; init; } = \"\"; }"));

        (HasDiagnostic(result, "PRAG0302") || HasDiagnostic(result, "PRAG0303")).Should().BeTrue(
            "neither ServiceFee nor Fee nor Invoice declares it, and a DTO member with no source is "
            + "the defect these diagnostics exist for");
    }
}
