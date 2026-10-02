using System.Linq;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     A <c>[Flags]</c> enum on both sides of a string.
/// </summary>
/// <remarks>
///     <para>
///         The specification said "no rule declared: what happens to <c>A, B</c> in a string is to be
///         defined". It turns out there is a rule and nobody had written it down — the mapping
///         delegates to <c>Enum.Parse</c> and <c>ToString</c>, and both have understood the
///         comma-separated form since .NET 1.
///     </para>
///     <para>
///         So this test is the declaration: the behaviour is measured rather than assumed, and now it
///         cannot change without something going red.
///     </para>
///     <para>
///         ⚠️ It does <b>not</b> combine with an alias. A switch over member names has no arm for
///         <c>A, B</c>, and inventing one would be a second parser beside the one the runtime already
///         has. See the alias tests.
///     </para>
/// </remarks>
public class EnumFlagsGeneratorTests : MappingGeneratorTestBase
{
    private const string Source = """
        using System;
        using Pragmatic.Mapping;
        using Pragmatic.Mapping.Attributes;

        namespace TestApp;

        [Flags]
        public enum Access { None = 0, Read = 1, Write = 2 }

        public class Document
        {
            public Access Access { get; set; }
        }

        [MapFrom<Document>]
        [MapTo<Document>]
        public partial class DocumentDto
        {
            public string Access { get; init; } = "";
        }
        """;

    /// <summary>
    ///     Both directions go through the runtime's own understanding of the combined form.
    /// </summary>
    [Fact]
    public void ACombinedValue_GoesThroughParseAndToString()
    {
        var result = RunGenerator(Source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("; ", GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GetGeneratedSource(result, "DocumentDto.Mapping");
        generated.Should().NotBeNull();

        generated!.Should().Contain("ToString()",
            "reading a combined value gives \"Read, Write\", which is the runtime's own form");
        generated.Should().Contain("Enum.Parse<global::TestApp.Access>",
            "and writing it back reads that same form");
    }
}
