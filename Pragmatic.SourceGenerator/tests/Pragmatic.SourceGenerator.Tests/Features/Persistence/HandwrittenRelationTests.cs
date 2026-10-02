using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     Relations are declared, not written: <c>PRAG0619</c> for a navigation or a key typed as a
///     property, <c>PRAG0635</c> for EF Core's relational attributes.
/// </summary>
/// <remarks>
///     The hand-written pair <c>Guid GuestId</c> + <c>Guest Guest</c> is not a second, equivalent way:
///     it is inferred from its shape with every option at its default, a key alone produces nothing,
///     and no constraint reaches the schema. Zero uses of the EF Core attributes do not mean they are
///     prevented: only a diagnostic that looks for them does.
/// </remarks>
public class HandwrittenRelationTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<System.ComponentModel.DataAnnotations.Schema.ForeignKeyAttribute>()
    ];

    private static SourceGenRunResult Run(string body)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>($$"""
            using System;
            using System.Collections.Generic;
            using System.ComponentModel.DataAnnotations.Schema;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Contoso.Booking;

            [Boundary]
            public partial class BookingBoundary;

            {{body}}
            """, References);

    [Fact]
    public void ANavigationWrittenAsAProperty_IsReported()
    {
        var result = Run("""
            [Entity]
            public partial class Guest : IEntity { }

            [Entity]
            public partial class Reservation : IEntity
            {
                public Guid GuestId { get; private set; }
                public Guest Guest { get; set; } = null!;
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0619").Should().BeTrue(
            "the pair used to be inferred from its shape, with every option at its default");
    }

    [Fact]
    public void AForeignKeyWrittenAlone_IsReported()
    {
        var result = Run("""
            [Entity]
            public partial class WorkItem : IEntity { }

            [Entity]
            public partial class Mention : IEntity
            {
                public Guid WorkItemId { get; private set; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0619").Should().BeTrue(
            "a key alone produced nothing: no navigation, no cascade, and no constraint in the schema");
    }

    [Fact]
    public void ACollectionWrittenAsAProperty_IsReported()
    {
        var result = Run("""
            [Entity]
            public partial class Guest : IEntity
            {
                public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
            }

            [Entity]
            public partial class Reservation : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0619").Should().BeTrue();
    }

    [Fact]
    public void AnEfCoreRelationalAttribute_IsReported()
    {
        var result = Run("""
            [Entity]
            public partial class Guest : IEntity { }

            [Entity]
            [Relation.ManyToOne<Guest>]
            public partial class Reservation : IEntity
            {
                [ForeignKey("GuestId")]
                public string Note { get; private set; } = "";
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0635").Should().BeTrue(
            "the generator never read [ForeignKey], so the database knew a relationship the model did not");
    }

    /// <summary>The declared form: nothing to report.</summary>
    [Fact]
    public void ADeclaredRelation_IsSilent()
    {
        var result = Run("""
            [Entity]
            public partial class Guest : IEntity { }

            [Entity]
            [Relation.ManyToOne<Guest>]
            public partial class Reservation : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0619").Should().BeFalse();
        GeneratorTestHelper.HasDiagnostic(result, "PRAG0635").Should().BeFalse();
    }

    /// <summary>
    ///     The control that keeps the key rule narrow: a scalar that ends in <c>Id</c> is a key only
    ///     when what precedes it names an entity. An external identifier is a column.
    /// </summary>
    [Fact]
    public void AScalarThatMerelyEndsInId_IsSilent()
    {
        var result = Run("""
            [Entity]
            public partial class Reservation : IEntity
            {
                public string ExternalId { get; private set; } = "";
                public Guid CorrelationId { get; private set; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0619").Should().BeFalse(
            "neither External nor Correlation is an entity");
    }
}
