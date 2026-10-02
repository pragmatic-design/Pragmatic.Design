using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     PRAG0624 — a trait's properties declared by hand, but only some of them.
/// </summary>
/// <remarks>
///     <para>
///         The generator stands down on a trait's property group when the entity declares all of it:
///         that is how an application owns the shape of its own audit or soft-delete columns. The
///         decision is per group, because the group is what the template emits.
///     </para>
///     <para>
///         Without this diagnostic, declaring three of the four says nothing at all. The group flag
///         stays false, all four are emitted, and the author gets a <c>CS0102</c> for each one they
///         wrote — blaming a duplicate in a generated file they never opened, with nothing to say that
///         writing the <b>missing</b> one is the fix. The obvious reading, deleting the ones that "already exist",
///         is the opposite of it.
///     </para>
/// </remarks>
public class PartialTraitPropertiesTests
{
    [Fact]
    public void ThreeOfTheFourAuditableProperties_IsReported()
    {
        var partial = Detect(isAuditable: true, declared: ["CreatedAt", "CreatedBy", "UpdatedAt"]);

        partial.Should().HaveCount(1);
        partial[0].TraitName.Should().Be("Auditable");
        // Naming what is missing is the whole value: CS0102 names the three that are present.
        partial[0].MissingProperties.AsImmutableArray().Should().Equal("UpdatedBy");
    }

    /// <summary>
    ///     All four declared: the entity owns the trait and the generator emits none of them.
    /// </summary>
    [Fact]
    public void AllFourAuditableProperties_IsAccepted()
        => Detect(isAuditable: true, declared: ["CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"])
            .Should().BeEmpty("declaring the whole group is how an application takes the trait over");

    /// <summary>
    ///     None declared: the ordinary case, and by far the most common one.
    /// </summary>
    [Fact]
    public void NoneOfTheAuditableProperties_IsAccepted()
        => Detect(isAuditable: true, declared: []).Should().BeEmpty();

    [Fact]
    public void TwoOfTheThreeSoftDeleteProperties_IsReported()
    {
        var partial = Detect(isSoftDelete: true, declared: ["IsDeleted", "DeletedAt"]);

        partial.Should().HaveCount(1);
        partial[0].TraitName.Should().Be("SoftDelete");
        partial[0].MissingProperties.AsImmutableArray().Should().Equal("DeletedBy");
    }

    /// <summary>
    ///     A property of a trait the entity does not have is just a property.
    /// </summary>
    /// <remarks>
    ///     An entity may well carry its own <c>DeletedAt</c> without <c>[SoftDelete]</c> — nothing is
    ///     generated for it, so nothing collides. Reporting it would condemn ordinary domain modelling
    ///     on the strength of a name.
    /// </remarks>
    [Fact]
    public void APropertyNamedLikeATraitTheEntityDoesNotHave_IsAccepted()
        => Detect(isSoftDelete: false, declared: ["DeletedAt"]).Should().BeEmpty();

    /// <summary>
    ///     Both traits half-declared: one finding each, so neither is hidden by the other.
    /// </summary>
    [Fact]
    public void BothTraitsPartlyDeclared_AreBothReported()
        => Detect(isAuditable: true, isSoftDelete: true, declared: ["CreatedAt", "IsDeleted"])
            .Select(p => p.TraitName).Should().Equal("Auditable", "SoftDelete");

    private static ImmutableArray<PartialTraitModel> Detect(
        bool isAuditable = false, bool isSoftDelete = false, string[]? declared = null)
        => Pragmatic.SourceGenerator.Features.Persistence.Transforms.EntityTransform
            .DetectPartialTraits(declared ?? [], isAuditable, isSoftDelete);
}
