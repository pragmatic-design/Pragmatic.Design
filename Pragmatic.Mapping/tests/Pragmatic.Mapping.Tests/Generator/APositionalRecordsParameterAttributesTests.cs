using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     On a positional record the mapping attributes are written on the parameter, and the mapping reads
///     them there.
/// </summary>
/// <remarks>
///     <c>[MapProperty]</c>, <c>[MapIgnore]</c> and <c>[MapConverter&lt;T&gt;]</c> declare
///     <c>AttributeTargets.Parameter</c>, so they compile on a record's parameter; but an attribute written
///     there stays on the parameter — the property the compiler synthesizes from it has none. A generator
///     that read only the property would fail a rename loudly (PRAG0303), and drop an ignore, a format or
///     a converter on a parameter named like its source in silence, copying the raw value.
/// </remarks>
public class APositionalRecordsParameterAttributesTests : MappingGeneratorTestBase
{
    private const string Source = """
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Mapping.Converters;

        namespace TestApp.Entities
        {
            public class Entry
            {
                public string Operation { get; set; } = "";
                public string? ActorRef { get; set; }
                public string Secret { get; set; } = "";
                public string Name { get; set; } = "";
            }
        }

        namespace TestApp.Dtos
        {
            public sealed class Shout : IValueConverter<string, string>
            {
                public string Convert(string source) => source.ToUpperInvariant();
                public string ConvertBack(string target) => target;
            }
        }
        """;

    private static string MappingOf(string record)
    {
        var result = RunGenerator(Source + "\nnamespace TestApp.Dtos { " + record + " }");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));
        HasDiagnostic(result, "PRAG0303").Should().BeFalse("the attribute on the parameter names the source");

        return GetGeneratedSourcesAsDictionary(result).First(kv => kv.Key.EndsWith("EntryDto.Mapping.g.cs")).Value;
    }

    [Fact]
    public void MapPropertyOnAParameter_NamesTheSource()
    {
        var mapping = MappingOf("""
            [MapFrom<TestApp.Entities.Entry>]
            public sealed partial record EntryDto(string Operation, [MapProperty("ActorRef")] string? DecidedBy);
            """);

        mapping.Should().Contain("entity.ActorRef");
    }

    [Fact]
    public void MapIgnoreOnAParameter_DoesNotReadTheSource()
    {
        var mapping = MappingOf("""
            [MapFrom<TestApp.Entities.Entry>]
            public sealed partial record EntryDto(string Operation, [MapIgnore] string? Secret);
            """);

        mapping.Should().NotContain("entity.Secret");
    }

    [Fact]
    public void MapConverterOnAParameter_GoesThroughTheConverter()
    {
        var mapping = MappingOf("""
            [MapFrom<TestApp.Entities.Entry>]
            public sealed partial record EntryDto(string Operation, [MapConverter<Shout>] string Name);
            """);

        mapping.Should().Contain("Shout");
    }

    /// <summary>The same attributes on a record's own properties, as they always worked.</summary>
    [Fact]
    public void OnAProperty_TheAttributesStillApply()
    {
        var mapping = MappingOf("""
            [MapFrom<TestApp.Entities.Entry>]
            public sealed partial record EntryDto
            {
                public string Operation { get; init; } = "";
                [MapProperty("ActorRef")] public string? DecidedBy { get; init; }
                [MapIgnore] public string? Secret { get; init; }
                [MapConverter<Shout>] public string Name { get; init; } = "";
            }
            """);

        mapping.Should().Contain("entity.ActorRef").And.Contain("Shout").And.NotContain("entity.Secret");
    }
}
