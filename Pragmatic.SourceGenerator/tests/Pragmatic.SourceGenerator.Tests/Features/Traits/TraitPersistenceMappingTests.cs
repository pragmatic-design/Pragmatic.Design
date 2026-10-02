using System.Collections.Immutable;
using System.Linq;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Pragmatic.SourceGenerator.Features.Traits.Models;
using Pragmatic.SourceGenerator.Features.Traits.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Traits;

/// <summary>
///     The trait EF configurations and the migrations schema are two descriptions of the same
///     tables. When they disagree the generated code still compiles and the tests still pass —
///     the failure only shows up against a real database. These tests pin the agreement:
///     every trait table is keyed on the <c>PersistenceId</c> column, the tag junction is keyed
///     on its two FKs and has no surrogate at all.
/// </summary>
public class TraitPersistenceMappingTests
{
    private static CommentTraitModel Comment() => new()
    {
        ParentTypeName = "Reservation",
        ParentNamespace = "Showcase.Booking.Entities",
        ParentFullTypeName = "Showcase.Booking.Entities.Reservation",
        IdType = "System.Guid",
        BoundaryFullTypeName = "Showcase.Booking.BookingBoundary",
        BoundaryName = "Booking",
        ResourceSegment = "reservations",
        ResourceParamName = "reservationId",
    };

    private static NoteTraitModel Note() => new()
    {
        ParentTypeName = "Guest",
        ParentNamespace = "Showcase.Booking.Entities",
        ParentFullTypeName = "Showcase.Booking.Entities.Guest",
        IdType = "System.Guid",
        BoundaryFullTypeName = "Showcase.Booking.BookingBoundary",
        BoundaryName = "Booking",
        ResourceSegment = "guests",
        ResourceParamName = "guestId",
    };

    private static AttachmentTraitModel Attachment() => new()
    {
        ParentTypeName = "Reservation",
        ParentNamespace = "Showcase.Booking.Entities",
        ParentFullTypeName = "Showcase.Booking.Entities.Reservation",
        IdType = "System.Guid",
        BoundaryFullTypeName = "Showcase.Booking.BookingBoundary",
        BoundaryName = "Booking",
        ResourceSegment = "reservations",
        ResourceParamName = "reservationId",
        MaxPerEntity = 20,
        MaxFileSizeBytes = 10_485_760,
        AllowedExtensions = "",
    };

    private static TagTraitModel Tag() => new()
    {
        ParentTypeName = "Reservation",
        ParentNamespace = "Showcase.Booking.Entities",
        ParentFullTypeName = "Showcase.Booking.Entities.Reservation",
        IdType = "System.Guid",
        BoundaryFullTypeName = "Showcase.Booking.BookingBoundary",
        BoundaryName = "Booking",
        ResourceSegment = "reservations",
        ResourceParamName = "reservationId",
    };

    public static TheoryData<string, string> IdKeyedConfigs() => new()
    {
        { "Comment", new CommentEntityConfigTemplate(Comment()).RenderOutput().Text },
        { "Note", new NoteEntityConfigTemplate(Note()).RenderOutput().Text },
        { "Attachment", new AttachmentEntityConfigTemplate(Attachment()).RenderOutput().Text },
        { "Tag", new TagEntityConfigTemplate(Tag()).RenderOutput().Text },
    };

    [Theory]
    [MemberData(nameof(IdKeyedConfigs))]
    public void TraitEntityConfig_MapsIdToPersistenceIdColumn(string trait, string source)
    {
        source.Should().Contain("builder.HasKey(e => e.Id);", $"{trait} is keyed on Id");
        source.Should().Contain("builder.Property(e => e.Id).HasColumnName(\"PersistenceId\");",
            $"{trait}'s schema emits the key column as PersistenceId");
        source.Should().Contain("builder.Ignore(e => e.PersistenceId);",
            $"{trait}'s PersistenceId is a computed alias of Id, not a second column");
    }

    [Theory]
    [MemberData(nameof(IdKeyedConfigs))]
    public void TraitEntityConfig_LeavesTheKeyValueGenerated(string trait, string source)
    {
        // The generated add/upload actions return entity.Id right after Add(), before SaveChanges:
        // only EF's client-side Guid generator can have filled it in. ValueGeneratedNever would
        // hand every row Guid.Empty and the second insert would collide on the PK.
        source.Should().NotContain("ValueGeneratedNever", $"{trait} must keep its key value-generated");
    }

    [Fact]
    public void TraitEntityConfig_IgnoresTheAbstractParentEntityId()
    {
        // ParentEntityId lives on the trait base class; the concrete FK is {Parent}Id and is the
        // only one the schema knows about.
        new NoteEntityConfigTemplate(Note()).RenderOutput().Text
            .Should().Contain("builder.Ignore(e => e.ParentEntityId);");
        new AttachmentEntityConfigTemplate(Attachment()).RenderOutput().Text
            .Should().Contain("builder.Ignore(e => e.ParentEntityId);");
        new TagJunctionConfigTemplate(Tag()).RenderOutput().Text
            .Should().Contain("builder.Ignore(e => e.ParentEntityId);");
    }

    [Fact]
    public void TagJunctionConfig_IsKeyedOnBothForeignKeys()
    {
        var source = new TagJunctionConfigTemplate(Tag()).RenderOutput().Text;

        source.Should().Contain("builder.HasKey(e => new { e.ReservationId, e.TagId });");
        source.Should().NotContain("PersistenceId", "the junction has no surrogate key");
    }

    [Fact]
    public void Schema_WithoutDeclaredKey_KeepsTheSurrogatePersistenceId()
    {
        var table = TransformSingle(BuildEntity("Reservation", keyColumns: []));

        table.Columns.AsImmutableArray().Should().ContainSingle(c => c.IsPrimaryKey)
            .Which.Name.Should().Be("PersistenceId");
    }

    [Fact]
    public void Schema_WithDeclaredKey_DropsTheSurrogateAndKeysTheDeclaredColumns()
    {
        var table = TransformSingle(BuildEntity("ReservationTagLink",
            keyColumns: ["ReservationId", "TagId"]));

        table.Columns.AsImmutableArray().Select(c => c.Name)
            .Should().NotContain("PersistenceId", "an insert could never satisfy a surrogate the EF model does not write");
        table.Columns.AsImmutableArray().Where(c => c.IsPrimaryKey).Select(c => c.Name)
            .Should().BeEquivalentTo("ReservationId", "TagId");
    }

    [Fact]
    public void Schema_DeclaredKeyColumn_IsNeverNullable()
    {
        var table = TransformSingle(BuildEntity("ReservationTagLink",
            keyColumns: ["ReservationId", "TagId"], nullableProperties: true));

        table.Columns.AsImmutableArray().Where(c => c.IsPrimaryKey)
            .Should().OnlyContain(c => !c.IsNullable);
    }

    private static TableSchemaModel TransformSingle(EntityMetadataModel entity)
        => SchemaMetadataTransform
            .Transform([entity], EfCoreProvider.PostgreSql, "TestDb", "Test")
            .Tables.AsImmutableArray()
            .Single(t => t.EntityTypeName == entity.FullTypeName);

    private static EntityMetadataModel BuildEntity(
        string typeName, string[] keyColumns, bool nullableProperties = false) => new()
    {
        TypeName = typeName,
        FullTypeName = $"Test.{typeName}",
        Namespace = "Test",
        IdType = "System.Guid",
        Accessibility = "public",
        IsValid = true,
        IsFromReference = true,
        KeyColumns = keyColumns.ToImmutableArray(),
        Properties = ImmutableArray.Create(
            new PropertyMetadataModel
            {
                Name = "ReservationId", TypeName = "System.Guid", IsNullable = nullableProperties
            },
            new PropertyMetadataModel
            {
                Name = "TagId", TypeName = "System.Guid", IsNullable = nullableProperties
            },
            new PropertyMetadataModel { Name = "AddedBy", TypeName = "string", IsNullable = true }),
    };
}
