using System.Linq;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Traits.Models;
using Pragmatic.SourceGenerator.Features.Traits.Templates;
using Pragmatic.SourceGenerator.Features.Traits.Transforms;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Traits;

/// <summary>
///     Pins the one thing that keeps a generated client honest: the DTO a consumer receives and the
///     description the manifest publishes are the same shape.
///     <para>
///     Two hand-maintained lists could agree by accident, but nothing would make them — a property
///     added to a template would leave the manifest describing the older shape, and a client
///     generated from that manifest would simply not know the field existed. Both render from
///     <see cref="TraitDtoShape"/>; these tests fail if anything starts bypassing it.
///     </para>
/// </summary>
public class TraitDtoShapeTests
{
    private static CommentTraitModel Comment() => new()
    {
        ParentTypeName = "Reservation",
        ParentNamespace = "Showcase.Booking.Entities",
        ParentFullTypeName = "Showcase.Booking.Entities.Reservation",
        IdType = "System.Guid",
        BoundaryName = "Booking",
        SupportInternalNotes = true,
    };

    private static NoteTraitModel Note() => new()
    {
        ParentTypeName = "Guest",
        ParentNamespace = "Showcase.Booking.Entities",
        ParentFullTypeName = "Showcase.Booking.Entities.Guest",
        IdType = "System.Guid",
        BoundaryName = "Booking",
    };

    private static TagTraitModel Tag() => new()
    {
        ParentTypeName = "Reservation",
        ParentNamespace = "Showcase.Booking.Entities",
        ParentFullTypeName = "Showcase.Booking.Entities.Reservation",
        IdType = "System.Guid",
        BoundaryName = "Booking",
    };

    private static AttachmentTraitModel Attachment() => new()
    {
        ParentTypeName = "Reservation",
        ParentNamespace = "Showcase.Booking.Entities",
        ParentFullTypeName = "Showcase.Booking.Entities.Reservation",
        IdType = "System.Guid",
        BoundaryName = "Booking",
    };

    [Fact]
    public void CommentDto_DeclaresAndProjects_EveryDescribedProperty()
        => AssertShapeIsHonoured(
            TraitDtoShape.Comment(Comment()),
            new CommentDtoTemplate(Comment()).RenderOutput().Text);

    [Fact]
    public void NoteDto_DeclaresAndProjects_EveryDescribedProperty()
        => AssertShapeIsHonoured(
            TraitDtoShape.Note(Note()),
            new NoteDtoTemplate(Note()).RenderOutput().Text);

    [Fact]
    public void TagDto_DeclaresAndProjects_EveryDescribedProperty()
        => AssertShapeIsHonoured(
            TraitDtoShape.Tag(Tag()),
            new TagDtoTemplate(Tag()).RenderOutput().Text);

    [Fact]
    public void AttachmentDto_DeclaresAndProjects_EveryDescribedProperty()
        => AssertShapeIsHonoured(
            TraitDtoShape.Attachment(Attachment()),
            new AttachmentDtoTemplate(Attachment()).RenderOutput().Text);

    [Fact]
    public void CommentDto_DeclaresNothingBeyondTheDescription()
    {
        var source = new CommentDtoTemplate(Comment()).RenderOutput().Text;
        var described = TraitDtoShape.Comment(Comment()).Select(p => p.Name).ToList();

        // Every `public … Name { get; init; }` in the DTO must be a property the manifest knows about.
        var declared = System.Text.RegularExpressions.Regex
            .Matches(source, @"public (?:required )?[^\s]+ (\w+) \{ get; init; \}")
            .Select(m => m.Groups[1].Value);

        declared.Should().BeSubsetOf(described,
            "a property the DTO declares but the manifest does not describe is invisible to a generated client");
    }

    private static void AssertShapeIsHonoured(
        System.Collections.Generic.IReadOnlyList<TraitDtoProperty> shape, string source)
    {
        shape.Should().NotBeEmpty();

        foreach (var property in shape)
        {
            source.Should().Contain($"{property.Type} {property.Name} {{ get; init; }}",
                $"the DTO must declare {property.Name} with the type the manifest publishes");
            source.Should().Contain($"{property.Name} = e.{property.Source},",
                $"the projection must fill {property.Name} from {property.Source}");
        }
    }
}
