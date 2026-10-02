using System.Collections.Immutable;
using System.Linq;
using Pragmatic.SourceGenerator.Features.Mapping.Templates;
using Pragmatic.SourceGenerator.Features.Resource.Models;
using Pragmatic.SourceGenerator.Features.Resource.Transforms;
using Pragmatic.SourceGenerator.Features.Resource.Templates;

namespace Pragmatic.SourceGenerator.Tests.Features.Resource;

/// <summary>
/// Verify snapshot tests for the [Resource] read DTOs, the read query and the write mutations.
///
/// The Create/Update input DTOs are gone: a mutation is its own input contract, so a separate type
/// described a body that already had one. The four CRUD DomainAction snapshots went with the actions
/// they pinned — writes are mutations and reads are Single queries now.
/// </summary>
public class ResourceSnapshotTests
{
    private static ResourceCrudModel BuildCrudModel() => new()
    {
        Resource = new ResourceModel
        {
            Namespace = "Showcase.Booking.Entities",
            TypeName = "Guest",
            FullTypeName = "global::Showcase.Booking.Entities.Guest",
            Segment = "guests",
            Capabilities = 63, // All
            BoundaryFullTypeName = "global::Showcase.Booking.BookingBoundary",
            BoundaryName = "Booking",
            IdType = "System.Guid",
        },
        Properties = ImmutableArray.Create(
            new ResourcePropertyInfo { Name = "Id", TypeName = "Guid", IsPrimaryKey = true },
            new ResourcePropertyInfo { Name = "FirstName", TypeName = "string", IsRequired = true },
            new ResourcePropertyInfo { Name = "LastName", TypeName = "string", IsRequired = true },
            new ResourcePropertyInfo { Name = "Email", TypeName = "string", IsRequired = true },
            new ResourcePropertyInfo { Name = "Phone", TypeName = "string?", IsNullable = true },
            new ResourcePropertyInfo { Name = "CreatedAt", TypeName = "DateTimeOffset", IsAudit = true },
            new ResourcePropertyInfo { Name = "UpdatedAt", TypeName = "DateTimeOffset?", IsAudit = true, IsNullable = true })
    };

    [Fact]
    public Task ReadDto_MatchesSnapshot()
    {
        var source = new ResourceDtoTemplate(BuildCrudModel(), ResourceDtoKind.Read).RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task ListItemDto_MatchesSnapshot()
    {
        var source = new ResourceDtoTemplate(BuildCrudModel(), ResourceDtoKind.ListItem).RenderOutput().Text;
        return Verify(source);
    }

    /// <summary>
    ///     The DTO declares the shape; the mapping that fills it is rendered by the Mapping feature from
    ///     a model this feature hands over. These two pin the half that moved — without them, taking
    ///     <c>Projection</c> and <c>FromEntity</c> out of the DTO snapshot would have deleted the only
    ///     coverage they had rather than relocating it.
    /// </summary>
    [Theory]
    [InlineData("Read")]
    [InlineData("ListItem")]
    public Task Mapping_MatchesSnapshot(string kind)
    {
        var model = ResourceMappingModelBuilder.Build(BuildCrudModel())
            .Single(m => m.TypeName == $"Guest{kind}Dto");

        var source = new MappingTemplate(model).RenderOutput().Text;
        return Verify(source).UseParameters(kind);
    }

    [Fact]
    public Task ReadQuery_MatchesSnapshot()
    {
        var source = new ResourceQueryTemplate(BuildCrudModel(), ResourceQueryKind.Read).RenderOutput().Text;
        return Verify(source);
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Update")]
    [InlineData("Delete")]
    public Task WriteMutation_MatchesSnapshot(string mode)
    {
        var model = ResourceMutationModelBuilder.Build(BuildCrudModel())
            .Single(m => m.Mode.ToString() == mode);

        var source = new ResourceMutationTemplate(model, mode).RenderOutput().Text;
        return Verify(source).UseParameters(mode);
    }
}
