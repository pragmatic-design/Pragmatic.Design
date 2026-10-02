using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     The four diagnostics that say "the projection does not do what <c>FromEntity</c> does".
/// </summary>
/// <remarks>
///     <para>
///         A converter, a format string, a <c>[MapDerived]</c> dispatch and a <c>[MapCondition]</c>
///         gate all run in memory and have no SQL. The projection keeps what it can translate and
///         drops the rest, and each drop has a diagnostic — <c>PRAG0320</c>, <c>0321</c>, <c>0331</c>,
///         <c>0332</c> — that nothing in the repository ever asserted. An emitted diagnostic no test
///         produces is indistinguishable from one that was switched off.
///     </para>
///     <para>
///         A converter or a format over a value read straight off the row is not dropped:
///         the projection computes it on the client after the read. What is still dropped, and still
///         reported, is one read through a navigation that may be null.
///     </para>
///     <para>
///         Each case comes with its control: the same shape without <c>[GenerateProjection]</c>, where
///         there is no projection to diverge from and the diagnostic must stay silent.
///     </para>
/// </remarks>
public class ProjectionDivergenceDiagnosticTests : MappingGeneratorTestBase
{
    /// <param name="projection">The type-level attribute that asks for a projection, or nothing.</param>
    /// <param name="dtoBody">The DTO's members.</param>
    /// <param name="extraTypes">Types the DTO needs beside <c>Source</c>.</param>
    private static string Source(string projection, string dtoBody, string extraTypes = "") => $$"""
        using System;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Mapping.Converters;

        namespace TestApp
        {
            public class Owner
            {
                public string Name { get; set; } = "";
                public DateTime BirthDate { get; set; }
            }

            public class Source
            {
                public string Name { get; set; } = "";
                public DateTime BirthDate { get; set; }
                public string? Email { get; set; }
                public Owner? Owner { get; set; }
            }

            {{extraTypes}}

            [MapFrom<Source>]
            {{projection}}
            public partial record Target
            {
                {{dtoBody}}
            }
        }
        """;

    private const string Converter = """
        public class UpperConverter : IValueConverter<string, string>
        {
            public string Convert(string source) => source.ToUpperInvariant();
            public string ConvertBack(string target) => target;
        }
        """;

    private const string ConvertedName = """
        [MapConverter<UpperConverter>]
        public string Name { get; init; } = "";
        """;

    [Fact]
    public void AConverterOverTheRow_IsCarried_AndNotReported()
    {
        var result = RunGenerator(Source("[GenerateProjection]", ConvertedName, Converter));

        HasDiagnostic(result, "PRAG0320").Should().BeFalse("the projection computes it on the client after the read");
        GetGeneratedSource(result, "Target.Mapping")!.Should().Contain("Name = ConvertAfterTheRead_Name(entity.Name),");
    }

    private const string ConvertedOwnerName = """
        [MapProperty("Owner.Name")]
        [MapConverter<UpperConverter>]
        public string OwnerName { get; init; } = "";
        """;

    [Fact]
    public void AConverterThroughANullableNavigation_ReportsPRAG0320()
    {
        HasDiagnostic(RunGenerator(Source("[GenerateProjection]", ConvertedOwnerName, Converter)), "PRAG0320")
            .Should().BeTrue("the client step would dereference a navigation that may be null, so the projection leaves it out");
    }

    [Fact]
    public void AConverterWithoutAProjection_IsNotReported()
    {
        HasDiagnostic(RunGenerator(Source("", ConvertedName, Converter)), "PRAG0320")
            .Should().BeFalse("there is no projection to drop it from");
    }

    private const string FormattedDate = """
        [MapProperty("BirthDate", Format = "yyyy-MM-dd")]
        public string BirthDate { get; init; } = "";
        """;

    [Fact]
    public void AFormatStringOverTheRow_IsCarried_AndNotReported()
    {
        HasDiagnostic(RunGenerator(Source("[GenerateProjection]", FormattedDate)), "PRAG0321")
            .Should().BeFalse("the projection formats it on the client after the read");
    }

    private const string FormattedOwnerDate = """
        [MapProperty("Owner.BirthDate", Format = "yyyy-MM-dd")]
        public string OwnerBirthDate { get; init; } = "";
        """;

    [Fact]
    public void AFormatStringThroughANullableNavigation_ReportsPRAG0321()
    {
        HasDiagnostic(RunGenerator(Source("[GenerateProjection]", FormattedOwnerDate)), "PRAG0321")
            .Should().BeTrue("a format through a navigation that may be null is still left out");
    }

    [Fact]
    public void AFormatStringWithoutAProjection_IsNotReported()
    {
        HasDiagnostic(RunGenerator(Source("", FormattedDate)), "PRAG0321").Should().BeFalse();
    }

    private const string Derived = """
        public class Special : Source { public string Extra { get; set; } = ""; }

        [MapFrom<Special>]
        public partial record SpecialTarget : Target { public string Extra { get; init; } = ""; }
        """;

    [Fact]
    public void AMapDerivedInAProjection_ReportsPRAG0331()
    {
        var source = Source("[GenerateProjection]\n    [MapDerived<Special, SpecialTarget>]",
            "public string Name { get; init; } = \"\";", Derived);

        HasDiagnostic(RunGenerator(source), "PRAG0331")
            .Should().BeTrue("the dispatch is a runtime type switch; the projection keeps the base shape");
    }

    [Fact]
    public void AMapDerivedWithoutAProjection_IsNotReported()
    {
        var source = Source("[MapDerived<Special, SpecialTarget>]",
            "public string Name { get; init; } = \"\";", Derived);

        HasDiagnostic(RunGenerator(source), "PRAG0331").Should().BeFalse();
    }

    private const string ConditionalEmail = """
        [MapCondition(nameof(HasEmail))]
        public string? Email { get; init; }

        private static bool HasEmail(Source source) => source.Email is not null;
        """;

    /// <summary>
    ///     A predicate with a block body cannot be written into the projection, which maps the property
    ///     unconditionally — and says so.
    /// </summary>
    [Fact]
    public void AMapConditionTheProjectionCannotInline_ReportsPRAG0332()
    {
        HasDiagnostic(RunGenerator(Source("[GenerateProjection]", ConditionalEmail.Replace(
                "private static bool HasEmail(Source source) => source.Email is not null;",
                "private static bool HasEmail(Source source) { return source.Email is not null; }"))), "PRAG0332")
            .Should().BeTrue("a block body gates FromEntity only; the projection maps the property unconditionally");
    }

    /// <summary>
    ///     An expression-bodied predicate gates the projection too, so there is no divergence
    ///     to report.
    /// </summary>
    [Fact]
    public void AMapConditionTheProjectionInlines_IsNotReported()
    {
        HasDiagnostic(RunGenerator(Source("[GenerateProjection]", ConditionalEmail)), "PRAG0332").Should().BeFalse();
    }

    [Fact]
    public void AMapConditionWithoutAProjection_IsNotReported()
    {
        HasDiagnostic(RunGenerator(Source("", ConditionalEmail)), "PRAG0332").Should().BeFalse();
    }
}
