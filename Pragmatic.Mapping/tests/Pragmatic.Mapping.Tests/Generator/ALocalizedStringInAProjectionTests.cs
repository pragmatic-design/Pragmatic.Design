using System.Linq;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     A <c>LocalizedString</c> read as a <c>string</c> is the reader's language, in a query
///     as well as in memory.
/// </summary>
/// <remarks>
///     The projection carried the implicit conversion, and the column is stored through a converter
///     whose provider type is <c>string</c>: EF Core took the conversion for "the column as stored" and
///     returned the JSON of every translation. <c>.Value</c> is evaluated on the client, after the
///     converter, in the request's culture.
/// </remarks>
public class ALocalizedStringInAProjectionTests : MappingGeneratorTestBase
{
    private const string Source = """
        using Pragmatic.Mapping.Attributes;

        namespace Pragmatic.Internationalization.Types
        {
            public sealed class LocalizedString
            {
                public string Value => "";
                public static implicit operator string(LocalizedString t) => t.Value;
            }
        }

        namespace Contoso
        {
            public class Kind { public Pragmatic.Internationalization.Types.LocalizedString Name { get; set; } = new(); }

            [MapFrom<Kind>]
            [GenerateProjection]
            public partial class KindDto { public string Name { get; init; } = ""; }
        }
        """;

    [Fact]
    public void TheProjection_ReadsTheValueInTheCurrentCulture()
    {
        var result = RunGenerator(Source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));
        var mapping = GetGeneratedSource(result, "KindDto.Mapping");
        mapping.Should().Contain("Name = entity.Name.Value,");
    }

    /// <summary>The control: in memory the implicit conversion already reads the value.</summary>
    [Fact]
    public void TheInMemoryMapping_IsUnchanged()
    {
        var result = RunGenerator(Source);

        var mapping = GetGeneratedSource(result, "KindDto.Mapping");
        mapping.Should().Contain("Name = entity.Name,");
    }
}
