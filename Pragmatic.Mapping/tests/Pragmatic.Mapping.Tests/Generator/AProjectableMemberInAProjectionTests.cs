using System.Linq;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     A projection that reads a <c>[Projectable]</c> member computes it in the query.
/// </summary>
/// <remarks>
///     The projection read the member's getter. EF Core cannot see into a getter, so it loaded the row
///     and ran the getter in memory, over a navigation nobody loaded: a sum over the children came back
///     zero, and nothing said so. The member's body is what the database can compute, so the body is
///     what the projection writes.
/// </remarks>
public class AProjectableMemberInAProjectionTests : MappingGeneratorTestBase
{
    private const string Source = """
        using System.Collections.Generic;
        using System.Linq;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Query.Attributes;

        namespace Pragmatic.Persistence.Query.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class ProjectableAttribute : System.Attribute { }
        }

        namespace Contoso
        {
            public enum LineStatus { Open, Paid }

            public class Line
            {
                public decimal Amount { get; set; }
                public LineStatus Status { get; set; }
            }

            public partial class Invoice
            {
                public decimal Discount { get; set; }
                public List<Line> Lines { get; set; } = new();

                [Projectable]
                public decimal Paid => Lines.Where(l => l.Status == LineStatus.Paid).Sum(l => l.Amount);

                [Projectable]
                public decimal Due => Lines.Sum(l => l.Amount) - Discount - Paid;
            }

            [MapFrom<Invoice>]
            [GenerateProjection]
            public partial class InvoiceDto
            {
                public decimal Discount { get; init; }
                public decimal Paid { get; init; }
                public decimal Due { get; init; }
            }
        }
        """;

    [Fact]
    public void TheProjection_ComputesTheMember_InsteadOfReadingItsGetter()
    {
        var result = RunGenerator(Source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));
        var projection = ProjectionOf(GetGeneratedSource(result, "InvoiceDto.Mapping"));
        projection.Should().Contain(
            "Paid = entity.Lines.Where(l => l.Status == global::Contoso.LineStatus.Paid).Sum(l => l.Amount),");
    }

    /// <summary>A projectable member that names another is computed through it, all the way down.</summary>
    [Fact]
    public void AMemberNamingAnother_IsComputedThroughIt()
    {
        var result = RunGenerator(Source);

        var projection = ProjectionOf(GetGeneratedSource(result, "InvoiceDto.Mapping"));
        projection.Should().Contain(
            "Due = entity.Lines.Sum(l => l.Amount) - entity.Discount - "
            + "(entity.Lines.Where(l => l.Status == global::Contoso.LineStatus.Paid).Sum(l => l.Amount)),");
    }

    /// <summary>The control: in memory the getter is the member, and a plain column is read as it is.</summary>
    [Fact]
    public void TheInMemoryMapping_AndAPlainColumn_AreUnchanged()
    {
        var result = RunGenerator(Source);

        var mapping = GetGeneratedSource(result, "InvoiceDto.Mapping");
        mapping.Should().Contain("Paid = entity.Paid,");
        ProjectionOf(mapping).Should().Contain("Discount = entity.Discount,");
    }

    /// <summary>A projectable member reached through a navigation, as a flattened DTO property or a nested DTO.</summary>
    private const string ThroughANavigation = """
        using System.Collections.Generic;
        using System.Linq;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Query.Attributes;

        namespace Pragmatic.Persistence.Query.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class ProjectableAttribute : System.Attribute { }
        }

        namespace Contoso
        {
            public enum LineStatus { Open, Paid }

            public class Line
            {
                public decimal Amount { get; set; }
                public LineStatus Status { get; set; }
            }

            public partial class Customer
            {
                public string Name { get; set; } = "";
                public List<Line> Lines { get; set; } = new();

                [Projectable]
                public decimal OpenBalance => Lines.Where(l => l.Status == LineStatus.Open).Sum(l => l.Amount);
            }

            public partial class Invoice
            {
                public decimal Discount { get; set; }
                public Customer Customer { get; set; } = new();
            }

            [MapFrom<Invoice>]
            [GenerateProjection]
            public partial class FlatInvoiceDto
            {
                public decimal Discount { get; init; }
                public decimal CustomerOpenBalance { get; init; }
            }

            [MapFrom<Customer>]
            public partial class CustomerDto
            {
                public string Name { get; init; } = "";
                public decimal OpenBalance { get; init; }
            }

            [MapFrom<Invoice>]
            [GenerateProjection]
            public partial class NestedInvoiceDto
            {
                public decimal Discount { get; init; }
                public CustomerDto? Customer { get; init; }
            }
        }
        """;

    /// <summary>The body of Customer.OpenBalance, as any projection that reaches it has to write it.</summary>
    private const string OpenBalanceBody =
        ".Lines.Where(l => l.Status == global::Contoso.LineStatus.Open).Sum(l => l.Amount)";

    /// <summary>
    ///     <c>CustomerOpenBalance</c> flattens to <c>entity.Customer.OpenBalance</c>, and
    ///     the member at the end of the path is the projectable one.
    /// </summary>
    [Fact]
    public void AFlattenedPath_ComputesTheMemberAtItsEnd()
    {
        var result = RunGenerator(ThroughANavigation);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));
        var projection = ProjectionOf(GetGeneratedSource(result, "FlatInvoiceDto.Mapping"));
        projection.Should().Contain("entity.Customer" + OpenBalanceBody);
        projection.Should().NotContain("entity.Customer.OpenBalance",
            "EF Core cannot see into the getter, and would run it over lines nobody loaded");
    }

    /// <summary>A nested DTO's inline initializer reads a projectable member of its own source.</summary>
    [Fact]
    public void ANestedDto_ComputesTheMemberInItsInitializer()
    {
        var result = RunGenerator(ThroughANavigation);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));
        var projection = ProjectionOf(GetGeneratedSource(result, "NestedInvoiceDto.Mapping"));
        projection.Should().Contain("entity.Customer!" + OpenBalanceBody);
        projection.Should().NotContain("OpenBalance = entity.Customer!.OpenBalance");
    }

    /// <summary>
    ///     The in-memory mapping reads the getter, and the getter walks <c>Lines</c> — so the DTO
    ///     declares it among the navigations to load, or the sum is over an empty collection.
    /// </summary>
    [Fact]
    public void TheInMemoryMapping_LoadsWhatTheGetterWalks()
    {
        var result = RunGenerator(Source);

        GetGeneratedSource(result, "InvoiceDto.Mapping")
            .Should().Contain("RequiredNavigations { get; } = [\"Lines\"];");
    }

    /// <summary>The same through a navigation: the path to the member, and what its getter walks from there.</summary>
    [Fact]
    public void AFlattenedPath_LoadsTheNavigationAndWhatTheGetterWalks()
    {
        var result = RunGenerator(ThroughANavigation);

        GetGeneratedSource(result, "FlatInvoiceDto.Mapping")
            .Should().Contain("RequiredNavigations { get; } = [\"Customer\", \"Customer.Lines\"];");
    }

    /// <summary>The control for both: a plain column reached the same way is read as it is.</summary>
    [Fact]
    public void APlainColumnThroughTheSameNavigation_IsReadAsItIs()
    {
        var result = RunGenerator(ThroughANavigation);

        ProjectionOf(GetGeneratedSource(result, "NestedInvoiceDto.Mapping"))
            .Should().Contain("Name = entity.Customer!.Name");
    }

    private static string ProjectionOf(string? mapping)
    {
        mapping.Should().NotBeNull();
        var start = mapping!.IndexOf("Projection { get; } =", System.StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "the DTO asks for a projection");
        return mapping[start..];
    }
}
