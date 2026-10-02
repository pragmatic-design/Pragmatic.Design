using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     PRAG0617 — with more than one relation to the same type, the inverse name has to be written.
/// </summary>
/// <remarks>
///     <para>
///         <c>PRAG0612</c> guards the name on the declaring side and goes quiet once they are all
///         written. The inverse has a default of its own — the declaring entity, pluralised — so two
///         relations to one type land on the same member of the target and the graph keeps the first.
///         The second relation does not exist, and nothing says so.
///     </para>
///     <para>
///         The self-referencing case is where it costs most: keys derived from the entity rather than
///         the navigations produce the same column twice and a primary key PostgreSQL refuses, so the
///         host does not start.
///     </para>
/// </remarks>
public class InverseNameRequiredTests
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

            namespace Contoso.Work;

            [Boundary]
            public partial class WorkBoundary;

            {{body}}
            """, References);

    [Fact]
    public void TwoRelationsWritingOnTheSameTarget_WithoutInverse_IsReported()
    {
        var result = Run("""
            [Entity]
            public partial class WorkItem : IEntity { }

            [Entity]
            [Relation.OneToMany<WorkItem>.WithNavigation("Authored")]
            [Relation.OneToMany<WorkItem>.WithNavigation("Reviewed")]
            public partial class Person : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0617").Should().BeTrue(
            "both derive the inverse from Person, so the second member is dropped and that relation "
            + "silently does not exist");
    }

    [Fact]
    public void TheSameTwo_WithInverseOnBoth_AreAccepted()
    {
        var result = Run("""
            [Entity]
            public partial class WorkItem : IEntity { }

            [Entity]
            [Relation.OneToMany<WorkItem>.WithNavigation("Authored", Inverse = "Author")]
            [Relation.OneToMany<WorkItem>.WithNavigation("Reviewed", Inverse = "Reviewer")]
            public partial class Person : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0617").Should().BeFalse(
            "naming the inverse is what makes the second relation exist");
    }

    /// <summary>The self-referencing shape, which is where the convention has the least to work with.</summary>
    [Fact]
    public void ASelfReferenceDeclaredTwice_WithoutInverse_IsReported()
    {
        var result = Run("""
            [Entity]
            [Relation.ManyToMany<Term>.WithNavigation("SeeAlso")]
            [Relation.ManyToMany<Term>.WithNavigation("SeenFrom")]
            public partial class Term : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0617").Should().BeTrue(
            "both ends are the same type, so nothing but an explicit name can tell them apart");
    }

    /// <summary>
    ///     The control that keeps the rule narrow: <c>ManyToOne</c> writes nothing on the target, so two
    ///     of them to one type cannot collide there and need no inverse.
    /// </summary>
    [Fact]
    public void TwoManyToOneToTheSameTarget_AreSilent()
    {
        var result = Run("""
            [Entity]
            public partial class Person : IEntity { }

            [Entity]
            [Relation.ManyToOne<Person>.WithNavigation("AssignedTo", ForeignKey = "AssignedToId")]
            [Relation.ManyToOne<Person>.WithNavigation("RequestedBy", ForeignKey = "RequestedById")]
            public partial class WorkItem : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0617").Should().BeFalse(
            "neither writes a member on Person, so there is no inverse to collide");
    }

    /// <summary>The second control: one relation to a type is never ambiguous.</summary>
    [Fact]
    public void ASingleRelation_IsSilent()
    {
        var result = Run("""
            [Entity]
            public partial class WorkItem : IEntity { }

            [Entity]
            [Relation.OneToMany<WorkItem>.WithNavigation("Items")]
            public partial class Person : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0617").Should().BeFalse();
    }
}
