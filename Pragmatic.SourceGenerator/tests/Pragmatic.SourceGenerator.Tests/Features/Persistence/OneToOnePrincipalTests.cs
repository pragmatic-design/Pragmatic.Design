using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     PRAG0618 — a one-to-one names exactly one principal.
/// </summary>
/// <remarks>
///     The principal is the end without the foreign key, and nothing but <c>IsPrincipal</c>
///     distinguishes two ends that declare the same attribute. With neither claiming it both behaved
///     as the dependent and the relationship got a key on each table; with both claiming it, it got
///     none. The generator's own test corpus contained the second shape — a navigation joined on
///     nothing — and compiled, so nothing was red.
/// </remarks>
public class OneToOnePrincipalTests
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

            namespace Contoso.Booking;

            [Boundary]
            public partial class BookingBoundary;

            {{body}}
            """, References);

    [Fact]
    public void NeitherEndClaimsToBeThePrincipal_IsReported()
    {
        var result = Run("""
            [Entity]
            [Relation.OneToOne<Preferences>.WithNavigation("Preferences")]
            public partial class Guest : IEntity { }

            [Entity]
            [Relation.OneToOne<Guest>.WithNavigation("Guest")]
            public partial class Preferences : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0618").Should().BeTrue(
            "with both ends dependent the relationship would carry a foreign key on each table");
    }

    [Fact]
    public void BothEndsClaimToBeThePrincipal_IsReported()
    {
        var result = Run("""
            [Entity]
            [Relation.OneToOne<Preferences>.WithNavigation("Preferences", IsPrincipal = true)]
            public partial class Guest : IEntity { }

            [Entity]
            [Relation.OneToOne<Guest>.WithNavigation("Guest", IsPrincipal = true)]
            public partial class Preferences : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0618").Should().BeTrue(
            "with both ends principal nothing carries the foreign key");
    }

    /// <summary>
    ///     A single declaration calling itself principal is the same defect: there is no second
    ///     declaration to hold the key. This is the shape the corpus contained.
    /// </summary>
    [Fact]
    public void TheOnlyDeclarationCallsItselfThePrincipal_IsReported()
    {
        var result = Run("""
            [Entity]
            [Relation.OneToOne<Preferences>.WithNavigation("Preferences", IsPrincipal = true)]
            public partial class Guest : IEntity { }

            [Entity]
            public partial class Preferences : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0618").Should().BeTrue(
            "nothing else declares this relationship, so no end is left to carry the key");
    }

    [Fact]
    public void ExactlyOnePrincipal_IsSilent()
    {
        var result = Run("""
            [Entity]
            [Relation.OneToOne<Preferences>.WithNavigation("Preferences", IsPrincipal = true)]
            public partial class Guest : IEntity { }

            [Entity]
            [Relation.OneToOne<Guest>.WithNavigation("Guest")]
            public partial class Preferences : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0618").Should().BeFalse();
    }

    /// <summary>
    ///     The control that keeps the rule narrow: declaring only from the dependent end is the
    ///     ordinary way to write a one-to-one, and the principal is the other end by elimination.
    /// </summary>
    [Fact]
    public void TheOnlyDeclarationIsTheDependent_IsSilent()
    {
        var result = Run("""
            [Entity]
            public partial class Guest : IEntity { }

            [Entity]
            [Relation.OneToOne<Guest>.WithNavigation("Guest")]
            public partial class Preferences : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0618").Should().BeFalse(
            "one declaration, and it is the end that carries the key");
    }
}
