using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     PRAG0610 — the child's navigation is one member, so it has one name.
/// </summary>
/// <remarks>
///     The parent may name it with <c>Inverse</c> and the child may name it with
///     <c>WithNavigation</c>. When both do and they disagree the relationship still resolves to a
///     single member, so one of the two written names produces nothing — the silent no-op that
///     <c>PRAG0611</c> exists to keep out, arriving through a different door.
/// </remarks>
public class InverseNameAgreementTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>()
    ];

    private static SourceGenRunResult Run(string body)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>($$"""
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Contoso.Billing;

            [Boundary]
            public partial class BillingBoundary;

            {{body}}
            """, References);

    [Fact]
    public void TheTwoEndsNameTheChildsNavigationDifferently_IsReported()
    {
        var result = Run("""
            [Entity]
            [Relation.ManyToOne<Invoice>.WithNavigation("Invoice")]
            public partial class Fee : IEntity { }

            [Entity]
            [Relation.OneToMany<Fee>.WithNavigation("Fees", Inverse = "Owner")]
            public partial class Invoice : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0610").Should().BeTrue(
            "one member cannot be called both Invoice and Owner, so one of the two names is dead");
    }

    [Fact]
    public void TheTwoEndsAgree_IsSilent()
    {
        var result = Run("""
            [Entity]
            [Relation.ManyToOne<Invoice>.WithNavigation("Owner")]
            public partial class Fee : IEntity { }

            [Entity]
            [Relation.OneToMany<Fee>.WithNavigation("Fees", Inverse = "Owner")]
            public partial class Invoice : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0610").Should().BeFalse();
    }

    /// <summary>
    ///     The control that keeps the rule narrow: <c>Inverse</c> naming a navigation the child does
    ///     not declare is the ordinary case — it is how the parent names the member it generates.
    /// </summary>
    [Fact]
    public void InverseWithNoDeclarationOnTheChild_IsSilent()
    {
        var result = Run("""
            [Entity]
            public partial class Fee : IEntity { }

            [Entity]
            [Relation.OneToMany<Fee>.WithNavigation("Fees", Inverse = "Owner")]
            public partial class Invoice : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0610").Should().BeFalse(
            "with nothing declared on the child there is no second name to contradict");
    }

    /// <summary>The second control: a child that declares its end but leaves the name to the parent.</summary>
    [Fact]
    public void TheChildDeclaresItsEndWithoutNamingIt_IsSilent()
    {
        var result = Run("""
            [Entity]
            [Relation.ManyToOne<Invoice>]
            public partial class Fee : IEntity { }

            [Entity]
            [Relation.OneToMany<Fee>.WithNavigation("Fees", Inverse = "Owner")]
            public partial class Invoice : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0610").Should().BeFalse(
            "the child named nothing, so Inverse is the only name and it is used");
    }
}
