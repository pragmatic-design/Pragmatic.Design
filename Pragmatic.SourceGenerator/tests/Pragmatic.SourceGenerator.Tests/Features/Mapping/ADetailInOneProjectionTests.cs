using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Mapping;

/// <summary>
///     A detail DTO read in one projection: nested references to other entities, a localized name in one
///     of them, and a nested object built from the row itself, present only when a condition holds.
/// </summary>
/// <remarks>
///     <para>
///         The shape of a leave request detail. Each section pins one behaviour:
///     </para>
///     <list type="bullet">
///         <item>a nested DTO's localized name is carried into the inlined projection;</item>
///         <item>a non-nullable nested DTO property gets no null assignment (CS8601);</item>
///         <item>a nested DTO over the same row maps from that row, not "no matching source property" (PRAG0303);</item>
///         <item><c>[MapCondition]</c> is honoured by the projection, not ignored (PRAG0332).</item>
///     </list>
/// </remarks>
public class ADetailInOneProjectionTests
{
    private const string Model = """
        using System;
        using Pragmatic.Internationalization.Types;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;

        namespace TestApp;

        public sealed class SalesBoundary { }

        public enum OrderStatus { Open, Approved }

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Customer : IEntity
        {
            public string Name { get; private set; } = "";
        }

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Kind : IEntity
        {
            public string Code { get; private set; } = "";
            public LocalizedString Name { get; private set; } = new();
        }

        [Entity]
        [BelongsTo<SalesBoundary>]
        [Relation.ManyToOne<Customer>.WithNavigation("Customer")]
        [Relation.ManyToOne<Customer>.WithNavigation("Approver", Required = false)]
        [Relation.ManyToOne<Kind>.WithNavigation("Kind")]
        public partial class Order : IEntity
        {
            public OrderStatus Status { get; private set; }
            public DateTimeOffset? ApprovedAt { get; private set; }
            public string? Note { get; private set; }
        }

        [PragmaticDbContext("Sales")]
        public partial class SalesDbContext { }

        [MapFrom<Customer>]
        public partial class CustomerReference
        {
            public Guid Id { get; init; }
            public string Name { get; init; } = "";
        }

        [MapFrom<Kind>]
        public partial class KindReference
        {
            public string Code { get; init; } = "";
            public string Name { get; init; } = "";
        }

        [MapFrom<Order>]
        public partial class Approval
        {
            public CustomerReference Approver { get; init; } = null!;
            public DateTimeOffset ApprovedAt { get; init; }

            [MapProperty(nameof(Order.Note))]
            public string? Comment { get; init; }
        }

        """;

    private const string Detail = """

        [MapFrom<Order>]
        [GenerateProjection]
        public partial class OrderDetail
        {
            public Guid Id { get; init; }
            public CustomerReference Customer { get; init; } = null!;
            public KindReference Kind { get; init; } = null!;

            [MapCondition(nameof(IsApproved))]
            public Approval? Approval { get; init; }

            // A condition on a member that is not nullable: the fallback is the one FromEntity writes,
            // which assigns no null a nullable context would flag.
            [MapCondition(nameof(IsApproved))]
            [MapProperty(nameof(Order.Note))]
            public string ApprovalNote { get; init; } = "";

            private static bool IsApproved(Order order) => order.Status == OrderStatus.Approved;
        }
        """;

    // =========================================================================
    // A nested localized name
    // =========================================================================

    /// <summary>A nested DTO reads a localized name as the top-level projection does: its value.</summary>
    [Fact]
    public void ANestedLocalizedName_IsReadAsItsValue()
    {
        Projection(Model + Detail).Should().Contain("Name = entity.Kind!.Name.Value");
    }

    /// <summary>The control: a plain string in a nested DTO is read as it is.</summary>
    [Fact]
    public void ANestedPlainString_IsReadAsItIs()
    {
        Projection(Model + Detail).Should().MatchRegex(@"Name = entity\.Customer!?\.Name\s*[,}]");
    }

    // =========================================================================
    // A non-nullable nested DTO
    // =========================================================================

    /// <summary>
    ///     A nested DTO the author declared non-nullable is not assigned null: the generated mapping
    ///     compiles with no warning, which an application building warnings as errors needs.
    /// </summary>
    [Fact]
    public void ANonNullableNestedDto_GeneratesNoNullAssignment()
    {
        var warnings = TraitCompilationHarness.CompileAndCollectWarnings(
            Model + Detail, file => file.EndsWith("OrderDetail.Mapping.g.cs", StringComparison.Ordinal)
                                    || file.EndsWith("Approval.Mapping.g.cs", StringComparison.Ordinal));

        warnings.Should().BeEmpty(TraitCompilationHarness.FormatErrors(warnings));
    }

    /// <summary>The control: a nullable nested DTO keeps its null check.</summary>
    [Fact]
    public void ANullableNestedDto_KeepsItsNullCheck()
    {
        var projection = Projection(Model + Detail.Replace(
            "public CustomerReference Customer { get; init; } = null!;",
            "public CustomerReference? Customer { get; init; }"));

        projection.Should().Contain("Customer = entity.Customer == null ? null : new");
    }

    // =========================================================================
    // A nested DTO over the same row
    // =========================================================================

    /// <summary>A nested DTO whose source is the same entity is built from the row itself.</summary>
    [Fact]
    public void ANestedDtoOverTheSameRow_IsBuiltFromTheRow()
    {
        var (sources, diagnostics) = TraitCompilationHarness.Generate(Model + Detail);

        diagnostics.Should().NotContain(d => d.Id == "PRAG0303" && d.GetMessage().Contains("Approval"));
        var mapping = Mapping(sources, "OrderDetail");
        mapping.Should().Contain("global::TestApp.Approval.FromEntity(entity)", "the in-memory mapping reads the same row");
        Projection(Model + Detail).Should().Contain("new global::TestApp.Approval { Approver = ");
    }

    /// <summary>
    ///     The control: a nested DTO over another entity, named after no navigation, is still reported.
    /// </summary>
    [Fact]
    public void ANestedDtoOverAnotherEntity_WithNoNavigation_IsStillReported()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Model + Detail.Replace(
            "public Guid Id { get; init; }",
            "public Guid Id { get; init; }\n    public CustomerReference? Buyer { get; init; }"));

        diagnostics.Should().Contain(d => d.Id == "PRAG0303" && d.GetMessage().Contains("Buyer"));
    }

    // =========================================================================
    // [MapCondition] in the projection
    // =========================================================================

    /// <summary>The condition gates the projection too, as its body, which the database can compute.</summary>
    [Fact]
    public void AConditionWithAnExpressionBody_GatesTheProjection()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Model + Detail);

        Projection(Model + Detail).Should().Contain(
            "Approval = (entity.Status == global::TestApp.OrderStatus.Approved) ? new global::TestApp.Approval");
        diagnostics.Should().NotContain(d => d.Id == "PRAG0332");
    }

    /// <summary>The control: a predicate the generator cannot inline still says the projection ignores it.</summary>
    [Fact]
    public void AConditionWithABlockBody_IsStillReportedAsIgnored()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Model + Detail.Replace(
            "private static bool IsApproved(Order order) => order.Status == OrderStatus.Approved;",
            "private static bool IsApproved(Order order) { return order.Status == OrderStatus.Approved; }"));

        diagnostics.Should().Contain(d => d.Id == "PRAG0332");
    }

    // =========================================================================
    // A nullable column into a non-nullable nested member
    // =========================================================================

    /// <summary>
    ///     A nested member that is not nullable, over a column that is, gets the default the top level
    ///     gives it — instead of an assignment that does not compile (CS0266).
    /// </summary>
    [Fact]
    public void ANullableColumn_IntoANonNullableNestedMember_IsDefaulted()
    {
        Projection(Model + Detail).Should().Contain("ApprovedAt = entity.ApprovedAt ?? default(DateTimeOffset)");
    }

    /// <summary>The control: a nullable nested member reads the column as it is.</summary>
    [Fact]
    public void ANullableColumn_IntoANullableNestedMember_IsReadAsItIs()
    {
        Projection(Model.Replace("public DateTimeOffset ApprovedAt { get; init; }", "public DateTimeOffset? ApprovedAt { get; init; }") + Detail)
            .Should().MatchRegex(@"ApprovedAt = entity\.ApprovedAt\s*[,}]");
    }

    // =========================================================================
    // A nested member the initializer cannot place
    // =========================================================================

    /// <summary>
    ///     A nested member whose source is neither a column, a nested DTO nor a list of them keeps the
    ///     DTO's default in the projection — and says so, instead of falling out of the loop in silence,
    ///     which is how a localized name once went missing.
    /// </summary>
    [Fact]
    public void ANestedMemberTheProjectionCannotPlace_ReportsPRAG0326()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(
            Model
                .Replace("public string Code { get; private set; } = \"\";",
                    "public string Code { get; private set; } = \"\";\n    public Palette Colors { get; private set; } = new();")
                .Replace("public string Code { get; init; } = \"\";",
                    "public string Code { get; init; } = \"\";\n    public Palette Colors { get; init; } = new();")
            + "\npublic sealed class Palette { public string Primary { get; set; } = \"\"; }\n"
            + Detail);

        diagnostics.Should().Contain(d => d.Id == "PRAG0326" && d.GetMessage().Contains("Kind"));
    }

    /// <summary>The control: a detail whose nested members all have a place reports nothing.</summary>
    [Fact]
    public void ANestedDtoWhoseMembersAllHaveAPlace_IsNotReported()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Model + Detail);

        diagnostics.Should().NotContain(d => d.Id == "PRAG0326");
    }

    // =========================================================================

    /// <summary>And the whole detail compiles: the mapping of the root and of every nested DTO.</summary>
    [Fact]
    public void TheDetail_Compiles()
    {
        var (errors, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Model + Detail, file => file.EndsWith(".Mapping.g.cs", StringComparison.Ordinal));

        errors.Should().BeEmpty(TraitCompilationHarness.FormatErrors(errors));
    }

    private static string Projection(string source)
    {
        var (sources, _) = TraitCompilationHarness.Generate(source);
        var mapping = Mapping(sources, "OrderDetail");
        var start = mapping.IndexOf("Projection { get; }", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the detail declares [GenerateProjection]");
        return mapping[start..];
    }

    private static string Mapping(Dictionary<string, string> sources, string dto)
    {
        var mapping = sources.FirstOrDefault(s => s.Key.EndsWith($".{dto}.Mapping.g.cs", StringComparison.Ordinal)).Value;
        mapping.Should().NotBeNull($"{dto} is mapped at all — otherwise every assertion is about nothing");
        return mapping!;
    }
}
