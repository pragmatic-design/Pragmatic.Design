using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     PRAG0611 — the delete behaviour is written where it is read.
/// </summary>
/// <remarks>
///     The graph reads a relationship's delete behaviour from the side that owns it, so that the two
///     generated configurations agree instead of depending on the order the entities were walked in.
///     A value written on the dependent side, where a counterpart exists, is therefore read by
///     nobody — the diagnostic is what keeps that from being a written instruction that silently does
///     nothing.
/// </remarks>
public class DeleteBehaviourOwnershipTests
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
    public void OnDeleteOnTheDependentSide_WhenTheOwnerDeclaresTheCollection_IsReported()
    {
        var result = Run("""
            [Entity]
            [Relation.ManyToOne<Invoice>.WithNavigation("Invoice", OnDelete = DeleteBehavior.Restrict)]
            public partial class Fee : IEntity { }

            [Entity]
            [Relation.OneToMany<Fee>]
            public partial class Invoice : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0611").Should().BeTrue(
            "the owning side describes the relationship, so this value is read by nobody");
    }

    /// <summary>
    ///     The control that keeps the rule narrow: with no collection on the other side there is no
    ///     owning declaration, and the unidirectional ManyToOne is the only thing describing the
    ///     relationship — setting the behaviour there is correct and common.
    /// </summary>
    [Fact]
    public void OnDeleteOnAManyToOneWithNoCounterpart_IsSilent()
    {
        var result = Run("""
            [Entity]
            [Relation.ManyToOne<Invoice>.WithNavigation("Invoice", OnDelete = DeleteBehavior.Restrict)]
            public partial class Fee : IEntity { }

            [Entity]
            public partial class Invoice : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0611").Should().BeFalse(
            "nothing else describes this relationship, so the declaration is where it is read");
    }

    /// <summary>Both sides declared, neither writing the value: the defaults are reconciled, not reported.</summary>
    [Fact]
    public void BothSidesDeclaredWithoutWritingIt_IsSilent()
    {
        var result = Run("""
            [Entity]
            [Relation.ManyToOne<Invoice>]
            public partial class Fee : IEntity { }

            [Entity]
            [Relation.OneToMany<Fee>]
            public partial class Invoice : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0611").Should().BeFalse(
            "the rule is about a value written where it is not read, not about declaring both ends");
    }

    /// <summary>The owning side is free to write it — that is the whole point of the rule.</summary>
    [Fact]
    public void OnDeleteOnTheOwningSide_IsSilent()
    {
        var result = Run("""
            [Entity]
            [Relation.ManyToOne<Invoice>]
            public partial class Fee : IEntity { }

            [Entity]
            [Relation.OneToMany<Fee>.WithNavigation("Fees", OnDelete = DeleteBehavior.Cascade)]
            public partial class Invoice : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0611").Should().BeFalse();
    }
}
