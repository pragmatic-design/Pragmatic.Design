using System.Linq;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     A member mapped with <c>Format</c> or <c>[MapConverter]</c> is carried by the projection,
///     computed on the client in the last step of the read, as <c>FromEntity</c> computes it.
/// </summary>
/// <remarks>
///     The projection kept only what SQL can translate and left the rest at the DTO's initialiser, so every
///     query over Showcase's <c>InvoiceSummaryDto</c> answered <c>"issuedDate": ""</c> and
///     <c>"totalFormatted": ""</c>. The projection is the executor's top-level <c>Select</c>, where EF Core
///     reads the source columns and evaluates what it cannot translate — the same step that already reads a
///     <c>LocalizedString</c>'s <c>.Value</c>.
/// </remarks>
public class AFormattedOrConvertedMemberInAProjectionTests : MappingGeneratorTestBase
{
    private const string Source = """
        using System;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Mapping.Converters;

        namespace Contoso
        {
            public class Invoice
            {
                public DateTimeOffset IssuedAt { get; set; }
                public DateTimeOffset? PaidAt { get; set; }
                public decimal Total { get; set; }
            }

            public sealed class MoneyConverter : IValueConverter<decimal, string>
            {
                public string Convert(decimal source) => source.ToString("N2");
                public decimal ConvertBack(string target) => decimal.Parse(target);
            }

            [MapFrom<Invoice>]
            [GenerateProjection]
            public partial class InvoiceDto
            {
                public decimal Total { get; init; }

                [MapProperty(nameof(Invoice.IssuedAt), Format = "yyyy-MM-dd")]
                public string IssuedDate { get; init; } = "";

                [MapProperty(nameof(Invoice.PaidAt), Format = "yyyy-MM-dd")]
                public string? PaidDate { get; init; }

                [MapProperty(nameof(Invoice.Total))]
                [MapConverter<MoneyConverter>]
                public string TotalFormatted { get; init; } = "";
            }
        }
        """;

    [Fact]
    public void AFormattedMember_IsInTheProjection()
        => Projection().Should().Contain(
            "IssuedDate = entity.IssuedAt.ToString(\"yyyy-MM-dd\", global::System.Globalization.CultureInfo.InvariantCulture),");

    [Fact]
    public void AConvertedMember_IsInTheProjection()
        => Projection().Should().Contain("TotalFormatted = ConvertAfterTheRead_TotalFormatted(entity.Total),");

    /// <summary>A nullable source is guarded the way an expression tree allows: no <c>?.</c> in it.</summary>
    [Fact]
    public void AFormattedNullableMember_IsInTheProjection_AsATernary()
        => Projection().Should().Contain(
            "PaidDate = entity.PaidAt == null ? null : entity.PaidAt.Value.ToString(\"yyyy-MM-dd\", global::System.Globalization.CultureInfo.InvariantCulture),");

    [Fact]
    public void TheProjection_Compiles_AndReportsNothingDropped()
    {
        var result = RunGenerator(Source);

        GetCompilationErrors(result).Select(d => d.ToString()).Should().BeEmpty();
        new[] { "PRAG0320", "PRAG0321", "PRAG0340" }.Where(id => HasDiagnostic(result, id))
            .Should().BeEmpty("every mapped member is carried now");
    }

    /// <summary>The control: the in-memory mapping is unchanged.</summary>
    [Fact]
    public void TheInMemoryMapping_IsUnchanged()
        => GetGeneratedSource(RunGenerator(Source), "InvoiceDto.Mapping")!
            .Should().Contain("PaidDate = entity.PaidAt?.ToString(\"yyyy-MM-dd\", global::System.Globalization.CultureInfo.InvariantCulture),");

    private static string Projection()
    {
        var mapping = GetGeneratedSource(RunGenerator(Source), "InvoiceDto.Mapping");
        mapping.Should().NotBeNull();
        var start = mapping!.IndexOf("Projection { get; }", System.StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1);
        return mapping[start..];
    }
}
