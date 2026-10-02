using System.Collections.Generic;
using System.Linq;
using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     [Relation.*] validation (PRAG0612-PRAG0615), driven through the REAL generator over real
///     source — a hand-built model cannot catch a rule that reads the target entity's symbols, which
///     is where every one of these rules lives.
/// </summary>
/// <remarks>
///     Each rule is paired with the nearest CORRECT shape. Those negative tests are the point: the
///     Showcase declares relations exactly like them, and a rule that fires there is mis-calibrated.
/// </remarks>
public class RelationDiagnosticsTests
{
    private static List<Diagnostic> Report(string entities, string id)
    {
        var source = "using Pragmatic.Persistence.Entity;\n\nnamespace Sales;\n\n" + entities;
        return TraitCompilationHarness.Generate(source).GeneratorDiagnostics
            .Where(d => d.Id == id)
            .ToList();
    }

    // =========================================================================
    // PRAG0612 — ambiguous relation
    // =========================================================================

    [Fact]
    public void Prag0612_SecondRelationToSameTargetUnnamed_IsReported()
    {
        var diagnostics = Report(
            """
            [Entity]
            public partial class Customer : IEntity { }

            [Entity]
            [Relation.ManyToOne<Customer>.WithNavigation("BillTo", ForeignKey = "BillToId")]
            [Relation.ManyToOne<Customer>]
            public partial class Order : IEntity { }
            """,
            "PRAG0612");

        diagnostics.Should().HaveCount(1, "only the relation that fails to name its navigation is at fault");
        diagnostics[0].GetMessage().Should().Contain("WithNavigation");
    }

    [Fact]
    public void Prag0612_EveryRelationToSameTargetNamed_IsNotReported()
    {
        // The Showcase shape (RoomAssignment → Guest twice, disambiguated by WithNavigation).
        var diagnostics = Report(
            """
            [Entity]
            public partial class Customer : IEntity { }

            [Entity]
            [Relation.ManyToOne<Customer>.WithNavigation("BillTo", ForeignKey = "BillToId")]
            [Relation.ManyToOne<Customer>.WithNavigation("ShipTo", ForeignKey = "ShipToId")]
            public partial class Order : IEntity { }
            """,
            "PRAG0612");

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Prag0612_SingleRelationPerTarget_IsNotReported()
    {
        var diagnostics = Report(
            """
            [Entity]
            public partial class Customer : IEntity { }

            [Entity]
            public partial class Warehouse : IEntity { }

            [Entity]
            [Relation.ManyToOne<Customer>]
            [Relation.ManyToOne<Warehouse>]
            public partial class Order : IEntity { }
            """,
            "PRAG0612");

        diagnostics.Should().BeEmpty();
    }

    // =========================================================================
    // PRAG0613 — inverse navigation not found
    // =========================================================================

    [Fact]
    public void Prag0613_InverseMatchesNothingOnTarget_IsReported()
    {
        var diagnostics = Report(
            """
            [Entity]
            [Relation.OneToMany<Order>.WithNavigation("Orders")]
            public partial class Customer : IEntity { }

            [Entity]
            [Relation.ManyToOne<Customer>.WithNavigation("Customer", Inverse = "Ordres")]
            public partial class Order : IEntity { }
            """,
            "PRAG0613");

        diagnostics.Should().HaveCount(1);
        diagnostics[0].GetMessage().Should().Contain("Ordres").And.Contain("Customer");
    }

    [Fact]
    public void Prag0613_InverseNamesNavigationTheTargetsOwnRelationGenerates_IsNotReported()
    {
        // The Showcase shape (Guest ↔ GuestPreferences): "Orders" exists only as a navigation the
        // generator will write onto Customer, so it is invisible as a symbol in this compilation.
        // Resolving declared members alone would flag correct code here.
        var diagnostics = Report(
            """
            [Entity]
            [Relation.OneToMany<Order>.WithNavigation("Orders")]
            public partial class Customer : IEntity { }

            [Entity]
            [Relation.ManyToOne<Customer>.WithNavigation("Customer", Inverse = "Orders")]
            public partial class Order : IEntity { }
            """,
            "PRAG0613");

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Prag0613_OneToManyInverseNamesTheNavigationItGenerates_IsNotReported()
    {
        // A OneToMany's Inverse DECLARES the child navigation — the generator creates it, so a name
        // that matches nothing yet is not an error.
        var diagnostics = Report(
            """
            [Entity]
            [Relation.OneToMany<Order>.WithNavigation("Orders", Inverse = "Parent")]
            public partial class Customer : IEntity { }

            [Entity]
            public partial class Order : IEntity { }
            """,
            "PRAG0613");

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Prag0613_LocationPointsAtTheOffendingAttribute()
    {
        var entities =
            """
            [Entity]
            [Relation.OneToMany<Order>.WithNavigation("Orders")]
            public partial class Customer : IEntity { }

            [Entity]
            [Relation.ManyToOne<Customer>.WithNavigation("Customer", Inverse = "Ordres")]
            public partial class Order : IEntity { }
            """;

        var source = "using Pragmatic.Persistence.Entity;\n\nnamespace Sales;\n\n" + entities;
        var diagnostic = TraitCompilationHarness.Generate(source).GeneratorDiagnostics
            .Single(d => d.Id == "PRAG0613");

        var span = diagnostic.Location.GetLineSpan();
        span.Path.Should().Be("TestSource.cs");

        var reportedLine = source.Replace("\r\n", "\n").Split('\n')[span.StartLinePosition.Line];
        reportedLine.Should().Contain("Inverse = \"Ordres\"",
            "the diagnostic must land on the attribute that is wrong, not on the file or the type");
    }

    // =========================================================================
    // PRAG0614 — inverse navigation of the wrong type
    // =========================================================================

    [Fact]
    public void Prag0614_InverseResolvesToACollectionOfAnotherEntity_IsReported()
    {
        var diagnostics = Report(
            """
            [Entity]
            public partial class Invoice : IEntity { }

            // The collection Customer calls "Orders" holds invoices: declared, so the generator
            // itself creates the member of the wrong type that Order's Inverse points at.
            [Entity]
            [Relation.OneToMany<Invoice>.WithNavigation("Orders")]
            public partial class Customer : IEntity { }

            [Entity]
            [Relation.ManyToOne<Customer>.WithNavigation("Customer", Inverse = "Orders")]
            public partial class Order : IEntity { }
            """,
            "PRAG0614");

        diagnostics.Should().HaveCount(1);
        diagnostics[0].GetMessage().Should().Contain("Invoice");
    }

    [Fact]
    public void Prag0614_InverseResolvesToACollectionOfTheDeclaringEntity_IsNotReported()
    {
        var diagnostics = Report(
            """
            [Entity]
            [Relation.OneToMany<Order>.WithNavigation("Orders")]
            public partial class Customer : IEntity { }

            [Entity]
            [Relation.ManyToOne<Customer>.WithNavigation("Customer", Inverse = "Orders")]
            public partial class Order : IEntity { }
            """,
            "PRAG0614");

        diagnostics.Should().BeEmpty();
    }

    // =========================================================================
    // PRAG0615 — duplicate navigation name
    // =========================================================================

    [Fact]
    public void Prag0615_TwoRelationsProducingTheSameNavigationName_IsReported()
    {
        var diagnostics = Report(
            """
            [Entity]
            public partial class Customer : IEntity { }

            [Entity]
            public partial class Supplier : IEntity { }

            [Entity]
            [Relation.ManyToOne<Customer>.WithNavigation("Party", ForeignKey = "CustomerId")]
            [Relation.ManyToOne<Supplier>.WithNavigation("Party", ForeignKey = "SupplierId")]
            public partial class Order : IEntity { }
            """,
            "PRAG0615");

        diagnostics.Should().HaveCount(1, "the first navigation wins; only the dropped one is reported");
        diagnostics[0].GetMessage().Should().Contain("Party");
    }

    [Fact]
    public void Prag0615_DistinctNavigationNames_IsNotReported()
    {
        var diagnostics = Report(
            """
            [Entity]
            public partial class Customer : IEntity { }

            [Entity]
            public partial class Supplier : IEntity { }

            [Entity]
            [Relation.ManyToOne<Customer>]
            [Relation.ManyToOne<Supplier>]
            public partial class Order : IEntity { }
            """,
            "PRAG0615");

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Prag0615_IsNotReportedOnTopOfPrag0612()
    {
        // Two unnamed relations to one target collide on the derived name too. "Add WithNavigation"
        // is the actionable message; a duplicate-name error alongside it would be noise.
        var diagnostics = Report(
            """
            [Entity]
            public partial class Customer : IEntity { }

            [Entity]
            [Relation.ManyToOne<Customer>]
            [Relation.ManyToOne<Customer>]
            public partial class Order : IEntity { }
            """,
            "PRAG0615");

        diagnostics.Should().BeEmpty();
    }

    // =========================================================================
    // Descriptors
    // =========================================================================

    [Fact]
    public void Descriptors_KeepTheirIdsAndSeverities()
    {
        PersistenceDiagnostics.AmbiguousRelation.Id.Should().Be("PRAG0612");
        PersistenceDiagnostics.AmbiguousRelation.DefaultSeverity.Should().Be(DiagnosticSeverity.Warning);

        PersistenceDiagnostics.InversePropertyNotFound.Id.Should().Be("PRAG0613");
        PersistenceDiagnostics.InversePropertyNotFound.DefaultSeverity.Should().Be(DiagnosticSeverity.Error);

        PersistenceDiagnostics.InversePropertyTypeMismatch.Id.Should().Be("PRAG0614");
        PersistenceDiagnostics.InversePropertyTypeMismatch.DefaultSeverity.Should().Be(DiagnosticSeverity.Error);

        PersistenceDiagnostics.DuplicateNavigationName.Id.Should().Be("PRAG0615");
        PersistenceDiagnostics.DuplicateNavigationName.DefaultSeverity.Should().Be(DiagnosticSeverity.Error);
    }

    [Fact]
    public void Descriptors_TellTheDeveloperWhatToDo()
    {
        // A diagnostic that only states the problem costs the reader a round trip to the source.
        PersistenceDiagnostics.AmbiguousRelation.MessageFormat.ToString().Should().Contain("WithNavigation");
        PersistenceDiagnostics.InversePropertyNotFound.MessageFormat.ToString().Should().Contain("declare the other side");
        PersistenceDiagnostics.InversePropertyTypeMismatch.MessageFormat.ToString().Should().Contain("point it at");
        PersistenceDiagnostics.DuplicateNavigationName.MessageFormat.ToString().Should().Contain("rename it");
    }
}
