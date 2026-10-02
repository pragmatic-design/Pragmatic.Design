using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     Persistence diagnostics that were defined and emitted, and asserted by no test.
/// </summary>
/// <remarks>
///     <para>
///         A descriptor nobody exercises is a claim: the reporting site can stop being reached — by a
///         guard added above it, by a model field that stopped being filled — and nothing turns red.
///         Each case here declares the shape that reports the diagnostic and pairs it with the nearest
///         shape that must not, because an assertion that only ever looks for a diagnostic is satisfied
///         by a generator that reports it always.
///     </para>
/// </remarks>
public class PersistenceDiagnosticEmissionTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.MultiTenancy.ITenantEntity>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>()
    ];

    private static SourceGenRunResult Run(string body) =>
        GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>($$"""
            using System;
            using System.Collections.Generic;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.MultiTenancy;
            using Pragmatic.Persistence.Entity;

            namespace Contoso.Sales;

            [Boundary]
            public partial class SalesBoundary;

            {{body}}
            """, References);

    // =========================================================================
    // PRAG0616 — a join entity whose foreign keys nobody named
    // =========================================================================

    /// <summary>
    ///     A many-to-many with an explicit join entity that names neither key is reported.
    /// </summary>
    /// <remarks>
    ///     The consequence is the most expensive in its chapter: EF invents shadow keys the migration
    ///     never creates, so the join table exists with the wrong columns and every write through the
    ///     relationship fails against the database.
    /// </remarks>
    [Fact]
    public void AJoinEntityWithUnnamedKeys_ReportsPrag0616()
    {
        var result = Run("""
            [Entity]
            [Relation.ManyToMany<Tag, OrderTag>.WithNavigation("Tags", Inverse = "Orders")]
            public partial class Order : IEntity { public string Reference { get; private set; } = ""; }

            [Entity]
            public partial class Tag : IEntity { public string Label { get; private set; } = ""; }

            [Entity]
            public partial class OrderTag : IEntity { public int Position { get; private set; } }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0616").Should().BeTrue(
            "neither LeftKey nor RightKey is named and the join entity declares no relation of its own, "
            + "so nothing says which column holds which end");
    }

    /// <summary>
    ///     The control: a join entity that declares both ends itself already names the keys.
    /// </summary>
    [Fact]
    public void AJoinEntityThatDeclaresBothEnds_IsSilent()
    {
        var result = Run("""
            [Entity]
            [Relation.ManyToMany<Tag, OrderTag>.WithNavigation("Tags", Inverse = "Orders")]
            public partial class Order : IEntity { public string Reference { get; private set; } = ""; }

            [Entity]
            public partial class Tag : IEntity { public string Label { get; private set; } = ""; }

            [Entity]
            [Relation.ManyToOne<Order>]
            [Relation.ManyToOne<Tag>]
            public partial class OrderTag : IEntity { public int Position { get; private set; } }
            """);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0616").Should().BeEmpty(
            "asking the author to repeat keys they already declared would report what the generator "
            + "resolves");
    }

    // =========================================================================
    // PRAG0625 — the parts of one domain key disagree about scope
    // =========================================================================

    [Fact]
    public void ACompositeDomainKeyWithMixedScopes_ReportsPrag0625()
    {
        var result = Run("""
            [Entity]
            public partial class Order : IEntity, ITenantEntity
            {
                public string TenantId { get; set; } = "";

                [LogicKey]
                public string Code { get; private set; } = "";

                [LogicKey(Scope = UniquenessScope.Global)]
                public string ExternalRef { get; private set; } = "";
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0625").Should().BeTrue(
            "one index has one scope, and either answer the generator picked would be a uniqueness "
            + "rule the author did not write");
    }

    [Fact]
    public void ACompositeDomainKeyWithOneScope_IsSilent()
    {
        var result = Run("""
            [Entity]
            public partial class Order : IEntity, ITenantEntity
            {
                public string TenantId { get; set; } = "";

                [LogicKey]
                public string Code { get; private set; } = "";

                [LogicKey]
                public string Line { get; private set; } = "";
            }
            """);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0625").Should().BeEmpty(
            "the parts agree, so there is nothing to choose between");
    }

    // =========================================================================
    // PRAG0626 — a tenant-scoped entity that assigns its own primary key
    // =========================================================================

    [Fact]
    public void ATenantEntityThatAssignsItsOwnKey_ReportsPrag0626()
    {
        var result = Run("""
            [Entity]
            public partial class Order : IEntity, ITenantEntity
            {
                public Guid PersistenceId { get; set; }
                public string TenantId { get; set; } = "";
                public string Reference { get; private set; } = "";
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0626").Should().BeTrue(
            "the tenant filter hides rows; it cannot make two identical primary keys coexist");
    }

    [Fact]
    public void ATenantEntityThatLetsTheGeneratorAssignTheKey_IsSilent()
    {
        var result = Run("""
            [Entity]
            public partial class Order : IEntity, ITenantEntity
            {
                public string TenantId { get; set; } = "";
                public string Reference { get; private set; } = "";
            }
            """);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0626").Should().BeEmpty(
            "a key the generator assigns is a version-7 Guid, unique across tenants by construction");
    }

    // =========================================================================
    // PRAG0710 / PRAG0711 — the loading profile's own warnings
    // =========================================================================
    //
    // These two had tests already, and they measured the condition on a hand-built model and the value
    // of the descriptor's Id constant. Neither ran the generator, so nothing said the reporting site is
    // still reached. That is what the two cases below add.

    private static SourceGenRunResult RunWithLoadWith(string declarations) =>
        GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>($$"""
            using System;
            using System.Collections.Generic;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query;

            namespace Contoso.Sales;

            [Boundary]
            public partial class SalesBoundary;

            [Entity]
            public partial class Order : IEntity { public string Reference { get; private set; } = ""; }

            {{declarations}}
            """, References);

    /// <summary>
    ///     A DTO member that reads as a navigation and matches none of the entity's is reported.
    /// </summary>
    [Fact]
    public void ADtoNavigationThatMatchesNothing_ReportsPrag0710()
    {
        var result = RunWithLoadWith("""
            public sealed class CustomerRef { public string Name { get; set; } = ""; }

            [LoadWith<Order>]
            public partial class OrderDto
            {
                public string Reference { get; set; } = "";
                public CustomerRef? Customer { get; set; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0710").Should().BeTrue(
            "the member will be null on every read, and an Include for it is the fix the message names");
    }

    /// <summary>The control: a DTO of scalars alone reports nothing.</summary>
    [Fact]
    public void ADtoOfScalarsAlone_ReportsNoPrag0710()
    {
        var result = RunWithLoadWith("""
            [LoadWith<Order>]
            public partial class OrderDto
            {
                public string Reference { get; set; } = "";
            }
            """);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0710").Should().BeEmpty(
            "nothing in it looks like a navigation");
    }

    /// <summary>A loading profile deeper than three levels is reported.</summary>
    [Fact]
    public void ALoadWithDeeperThanThreeLevels_ReportsPrag0711()
    {
        var result = RunWithLoadWith("""
            [LoadWith<Order>(MaxDepth = 4)]
            public partial class OrderDto
            {
                public string Reference { get; set; } = "";
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0711").Should().BeTrue(
            "each level multiplies the rows the join returns, and four is where it stops being a read");
    }

    /// <summary>The control: the default depth reports nothing.</summary>
    [Fact]
    public void ALoadWithAtTheDefaultDepth_ReportsNoPrag0711()
    {
        var result = RunWithLoadWith("""
            [LoadWith<Order>]
            public partial class OrderDto
            {
                public string Reference { get; set; } = "";
            }
            """);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0711").Should().BeEmpty();
    }

    // =========================================================================
    // PRAG0716 — a loading profile that reaches four navigations or more
    // =========================================================================

    private static SourceGenRunResult RunWideEntity(string dto) =>
        GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>($$"""
            using System;
            using System.Collections.Generic;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query;

            namespace Contoso.Sales;

            [Boundary]
            public partial class SalesBoundary;

            [Entity]
            [Relation.OneToMany<OrderLine>.WithNavigation("Lines")]
            [Relation.OneToMany<OrderNote>.WithNavigation("Notes")]
            [Relation.ManyToOne<Customer>.WithNavigation("Customer")]
            [Relation.ManyToOne<Address>.WithNavigation("ShipTo")]
            public partial class Order : IEntity { public string Reference { get; private set; } = ""; }

            [Entity]
            public partial class OrderLine : IEntity { public int Quantity { get; private set; } }

            [Entity]
            public partial class OrderNote : IEntity { public string Text { get; private set; } = ""; }

            [Entity]
            public partial class Customer : IEntity { public string Name { get; private set; } = ""; }

            [Entity]
            public partial class Address : IEntity { public string City { get; private set; } = ""; }

            {{dto}}
            """, References);

    [Fact]
    public void ALoadWithReachingFourNavigations_ReportsPrag0716()
    {
        var result = RunWideEntity("""
            [LoadWith<Order>]
            public partial class OrderDto
            {
                public string Reference { get; set; } = "";
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0716").Should().BeTrue(
            "four navigations on one read is a cartesian product the author did not ask for, and the "
            + "message is the place it gets said");
    }

    /// <summary>The control: an entity with fewer navigations reports nothing.</summary>
    [Fact]
    public void ALoadWithOverANarrowEntity_ReportsNoPrag0716()
    {
        var result = RunWithLoadWith("""
            [LoadWith<Order>]
            public partial class OrderDto
            {
                public string Reference { get; set; } = "";
            }
            """);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0716").Should().BeEmpty();
    }
}
