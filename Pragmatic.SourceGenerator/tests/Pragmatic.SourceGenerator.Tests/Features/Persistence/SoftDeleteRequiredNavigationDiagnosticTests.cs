using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     PRAG0705 — a required reference navigation to a <c>[SoftDelete]</c> entity. EF Core turns it into an
///     INNER JOIN, so the target's <c>!IsDeleted</c> filter also removes the dependent rows from every
///     query that joins the navigation: the rows stay in the table and become invisible.
///     <para>
///     The silence tests are the point of the rule: the same shape is intended (an owning parent) or
///     impossible (cross-boundary, where no EF relationship is configured at all), and reporting it would
///     make the diagnostic noise. They are modelled on real Showcase entities.
///     </para>
/// </summary>
public class SoftDeleteRequiredNavigationDiagnosticTests
{
    /// <summary>
    ///     Attribute surface the generator binds to: [Entity], [SoftDelete], [BelongsTo&lt;T&gt;] and the
    ///     nested [Relation.*] container. PragmaticDbContextAttribute flips HasPersistenceEFCore.
    /// </summary>
    private const string Stubs = """
        namespace Pragmatic.Persistence.EFCore
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class PragmaticDbContextAttribute : System.Attribute { }
        }

        namespace Pragmatic.Persistence.Entity
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class EntityAttribute : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class SoftDeleteAttribute : System.Attribute { public bool Cascade { get; set; } }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class BelongsToAttribute<TBoundary> : System.Attribute { }

            public static class Relation
            {
                [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
                public sealed class OneToMany<TRelated> : System.Attribute where TRelated : class
                {
                    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
                    public sealed class WithNavigation : System.Attribute
                    {
                        public WithNavigation(string name) { Name = name; }
                        public string Name { get; }
                        public string? Inverse { get; set; }
                    }
                }

                [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
                public sealed class ManyToOne<TRelated> : System.Attribute where TRelated : class
                {
                    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
                    public sealed class WithNavigation : System.Attribute
                    {
                        public WithNavigation(string name) { Name = name; }
                        public string Name { get; }
                        public string? Inverse { get; set; }
                        public string? ForeignKey { get; set; }
                        public bool Required { get; set; } = true;
                    }
                }
            }
        }
        """;

    private static SourceGenRunResult Run(string body)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Stubs + "\n" + body);

    private static bool HasSoftDeleteWarning(SourceGenRunResult result)
        => GeneratorTestHelper.HasDiagnostic(result, "PRAG0705");

    // =====================================================================
    // Fires — the dangerous shape
    // =====================================================================

    /// <summary>
    ///     A bare reference: Ticket points at a soft-deletable Customer, Customer knows nothing about
    ///     Ticket, and Ticket has no IsDeleted of its own. Soft-deleting one customer makes every ticket
    ///     that references them vanish from joined queries, permanently and irrecoverably.
    /// </summary>
    [Fact]
    public void RequiredReferenceToSoftDeletableTarget_ReportsPRAG0705()
    {
        var result = Run("""
            namespace Support
            {
                using Pragmatic.Persistence.Entity;

                public sealed class SupportBoundary { }

                [Entity]
                [SoftDelete]
                [BelongsTo<SupportBoundary>]
                public partial class Customer { public string Name { get; private set; } = ""; }

                [Entity]
                [BelongsTo<SupportBoundary>]
                [Relation.ManyToOne<Customer>]
                public partial class Ticket { public string Subject { get; private set; } = ""; }
            }
            """);

        HasSoftDeleteWarning(result).Should().BeTrue(
            "Ticket.Customer is required and Customer carries the soft-delete query filter");

        var diagnostic = GeneratorTestHelper
            .GetGeneratorDiagnostics(result, "PRAG0705")
            .Should().ContainSingle().Subject;

        diagnostic.GetMessage().Should().Contain("Ticket").And.Contain("Customer");
        diagnostic.Location.Should().NotBe(Location.None,
            "the diagnostic must point at the entity declaration, not at nothing");
        diagnostic.Location.GetLineSpan().Path.Should().Be("TestSource.cs");
    }

    // =====================================================================
    // Silent — the correct shapes next door
    // =====================================================================

    /// <summary>The nearby correct case: an OPTIONAL navigation to the same soft-deletable entity.</summary>
    [Fact]
    public void OptionalReferenceToSoftDeletableTarget_IsSilent()
    {
        var result = Run("""
            namespace Support
            {
                using Pragmatic.Persistence.Entity;

                public sealed class SupportBoundary { }

                [Entity]
                [SoftDelete]
                [BelongsTo<SupportBoundary>]
                public partial class Customer { public string Name { get; private set; } = ""; }

                [Entity]
                [BelongsTo<SupportBoundary>]
                [Relation.ManyToOne<Customer>.WithNavigation("Customer", Required = false)]
                public partial class Ticket { public string Subject { get; private set; } = ""; }
            }
            """);

        HasSoftDeleteWarning(result).Should().BeFalse(
            "an optional navigation is a LEFT JOIN — the dependent survives with a null reference");
    }

    /// <summary>
    ///     The Showcase shape (Invoice → LineItem, Reservation → RoomAssignment): the soft-deletable parent
    ///     declares the inverse collection, so the dependent is a child of its aggregate and disappearing
    ///     with the parent is the declared intent.
    /// </summary>
    [Fact]
    public void RequiredReferenceToOwningSoftDeletableParent_IsSilent()
    {
        var result = Run("""
            namespace Billing
            {
                using Pragmatic.Persistence.Entity;

                public sealed class BillingBoundary { }

                [Entity]
                [SoftDelete]
                [BelongsTo<BillingBoundary>]
                [Relation.OneToMany<LineItem>]
                public partial class Invoice { public string Number { get; private set; } = ""; }

                [Entity]
                [BelongsTo<BillingBoundary>]
                public partial class LineItem { public string Description { get; private set; } = ""; }
            }
            """);

        HasSoftDeleteWarning(result).Should().BeFalse(
            "[Relation.OneToMany<LineItem>] declares Invoice as the owner — hiding its children is intended");
    }

    /// <summary>
    ///     Cross-boundary: EntityConfigurationTemplate skips the navigation and no CLR navigation property is
    ///     generated, so only the raw FK column survives. With no relationship in the EF model there is no
    ///     join and no filter to propagate.
    /// </summary>
    [Fact]
    public void RequiredCrossBoundaryReferenceToSoftDeletableTarget_IsSilent()
    {
        var result = Run("""
            namespace Catalog
            {
                using Pragmatic.Persistence.Entity;

                public sealed class CatalogBoundary { }

                [Entity]
                [SoftDelete]
                [BelongsTo<CatalogBoundary>]
                public partial class Property { public string Code { get; private set; } = ""; }
            }

            namespace Booking
            {
                using Pragmatic.Persistence.Entity;

                public sealed class BookingBoundary { }

                [Entity]
                [BelongsTo<BookingBoundary>]
                [Relation.ManyToOne<Catalog.Property>]
                public partial class Reservation { public string Reference { get; private set; } = ""; }
            }
            """);

        HasSoftDeleteWarning(result).Should().BeFalse(
            "a cross-boundary relation produces only an FK column — no EF relationship, no INNER JOIN");
    }

    /// <summary>A required navigation to a target that is not soft-deletable has no filter to propagate.</summary>
    [Fact]
    public void RequiredReferenceToPlainTarget_IsSilent()
    {
        var result = Run("""
            namespace Support
            {
                using Pragmatic.Persistence.Entity;

                public sealed class SupportBoundary { }

                [Entity]
                [BelongsTo<SupportBoundary>]
                public partial class Customer { public string Name { get; private set; } = ""; }

                [Entity]
                [BelongsTo<SupportBoundary>]
                [Relation.ManyToOne<Customer>]
                public partial class Ticket { public string Subject { get; private set; } = ""; }
            }
            """);

        HasSoftDeleteWarning(result).Should().BeFalse("Customer carries no soft-delete query filter");
    }

    /// <summary>
    ///     A soft-deletable dependent has its own IsDeleted lifecycle (and is reachable by
    ///     <c>[SoftDelete(Cascade = true)]</c>), so its rows can be marked and restored.
    /// </summary>
    [Fact]
    public void SoftDeletableDependent_IsSilent()
    {
        var result = Run("""
            namespace Support
            {
                using Pragmatic.Persistence.Entity;

                public sealed class SupportBoundary { }

                [Entity]
                [SoftDelete]
                [BelongsTo<SupportBoundary>]
                public partial class Customer { public string Name { get; private set; } = ""; }

                [Entity]
                [SoftDelete]
                [BelongsTo<SupportBoundary>]
                [Relation.ManyToOne<Customer>]
                public partial class Ticket { public string Subject { get; private set; } = ""; }
            }
            """);

        HasSoftDeleteWarning(result).Should().BeFalse(
            "the dependent has its own soft-delete lifecycle — the rows are reachable and restorable");
    }

    [Fact]
    public void Descriptor_IsAWarningInTheQueryPipelineRange()
    {
        QueryPipelineDiagnostics.SoftDeleteRequiredNavigation.Id.Should().Be("PRAG0705");
        QueryPipelineDiagnostics.SoftDeleteRequiredNavigation.DefaultSeverity
            .Should().Be(DiagnosticSeverity.Warning);
    }
}
