using System.Collections.Immutable;
using System.Linq;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Resource.Models;
using Pragmatic.SourceGenerator.Features.Resource.Transforms;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Resource;

/// <summary>
///     The write half of <c>[Resource]</c>, as mutations.
/// </summary>
/// <remarks>
///     <para>
///         Mutations, not <c>DomainAction</c>s whose entire body is <c>new Entity()</c>, a few setters
///         and <c>_repository.Add</c> — that would be a second write path with no validation, no
///         permission requirement, no commit strategy and no domain-event hand-over, and the same write
///         would behave differently depending on who performed it.
///     </para>
///     <para>
///         What is asserted here is what silently goes wrong otherwise: an input list that leaks
///         ownership or audit columns into the request body, an update that blanks every field the
///         caller did not send, a delete with no id to load by, and a permission nobody would notice
///         was missing until it was not enforced.
///     </para>
/// </remarks>
public class ResourceMutationModelBuilderTests
{
    private const int Create = 1, Update = 4, Delete = 8, Restore = 64;

    [Fact]
    public void TheWriteCapabilities_BecomeMutationsInTheirModes()
        => Build(Create | Update | Delete).Select(m => m.Mode).Should().Equal(
            MutationModeValue.Create, MutationModeValue.Update, MutationModeValue.Delete);

    [Fact]
    public void EachOne_IsNamedForTheOperationSoADeveloperCanDeclareItsPartialPart()
        => Build(Create | Update | Delete).Select(m => m.TypeName).Should().Equal(
            "ResourceCreateGuestMutation", "ResourceUpdateGuestMutation", "ResourceDeleteGuestMutation");

    // ── Input shape ───────────────────────────────────────────────────────

    /// <summary>
    ///     Create takes the domain fields and nothing else.
    /// </summary>
    /// <remarks>
    ///     The primary key, the audit columns and the ownership columns are the server's to set. A
    ///     request body carrying <c>OwnerId</c> lets a caller assign someone else's row to themselves,
    ///     and one carrying <c>CreatedBy</c> lets them forge an author — neither shows up in a test that
    ///     only checks the happy path, and both are visible in the OpenAPI document for anyone looking.
    /// </remarks>
    [Fact]
    public void Create_TakesTheDomainFieldsOnly()
        => Single(Create).InputProperties.Select(p => p.Name).Should().Equal("Name", "Email");

    [Fact]
    public void Create_HasNoId_BecauseThereIsNoRowYet()
        => Single(Create).IdPropertyName.Should().BeNull();

    /// <summary>
    ///     Every other mode loads an existing row, and the invoker finds it through <c>Id</c>.
    /// </summary>
    /// <remarks>
    ///     Without it the generated endpoint has nowhere to put the route value, and the failure is a
    ///     compile error inside a file the author cannot edit.
    /// </remarks>
    [Theory]
    [InlineData(Update)]
    [InlineData(Delete)]
    public void EveryModeThatLoadsARow_DeclaresAnId(int capability)
    {
        var mutation = Single(capability);

        mutation.IdPropertyName.Should().Be("Id");
        mutation.InputProperties[0].Name.Should().Be("Id");
    }

    /// <summary>
    ///     Update is a patch: every field optional, so an absent one is not an instruction to blank it.
    /// </summary>
    [Fact]
    public void Update_MakesEveryFieldOptional()
    {
        var mutation = Single(Update);

        mutation.InputProperties.Where(p => p.Name != "Id").Should().AllSatisfy(p =>
        {
            p.IsRequired.Should().BeFalse();
            p.TypeName.Should().EndWith("?");
        });

        mutation.MappedProperties.Should().AllSatisfy(m => m.IsNullable.Should().BeTrue(
            "the generated ApplyToEntity guards a nullable mapping with 'is { } value'"));
    }

    [Fact]
    public void Delete_TakesNothingButTheId()
        => Single(Delete).InputProperties.Select(p => p.Name).Should().Equal("Id");

    // ── Restore ───────────────────────────────────────────────────────────

    [Fact]
    public void Restore_IsScaffoldedForASoftDeletableEntity()
        => Single(Restore, softDelete: true).Mode.Should().Be(MutationModeValue.Restore);

    /// <summary>
    ///     And skipped where there is nothing to put back.
    /// </summary>
    /// <remarks>
    ///     <c>ResourceCapabilities.All</c> includes Restore, so this is what lets "everything" keep
    ///     meaning "everything that applies" instead of scaffolding a mutation whose Restore mode the
    ///     invoker cannot run — <c>MutationMode.Restore</c> resets <c>IsDeleted</c> on an entity that
    ///     has no such column.
    /// </remarks>
    [Fact]
    public void Restore_IsSkippedWhenTheEntityIsNotSoftDeletable()
        => Build(Restore, softDelete: false).Should().BeEmpty();

    // ── Permissions ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(Create, "booking.guest.create")]
    [InlineData(Update, "booking.guest.update")]
    [InlineData(Delete, "booking.guest.delete")]
    public void EachWrite_RequiresTheEntityPermissionForItsVerb(int capability, string expected)
        => Single(capability).RequireAllPermissions.AsImmutableArray().Should().Equal(expected);

    /// <summary>
    ///     Restoring requires the delete permission, not one of its own.
    /// </summary>
    /// <remarks>
    ///     Undoing a delete is within the reach of whoever may delete. A verb of its own would need a
    ///     constant no producer emits, and an unresolvable permission is one that is never enforced —
    ///     which is what PRAG0418 exists to catch.
    /// </remarks>
    [Fact]
    public void Restore_RequiresTheDeletePermission()
        => Single(Restore, softDelete: true).RequireAllPermissions.AsImmutableArray()
            .Should().Equal("booking.guest.delete");

    // ── Helpers ───────────────────────────────────────────────────────────

    private static MutationModel Single(int capabilities, bool softDelete = false)
        => Build(capabilities, softDelete).Should().ContainSingle().Subject;

    private static ImmutableArray<MutationModel> Build(int capabilities, bool softDelete = false)
        => ResourceMutationModelBuilder.Build(new ResourceCrudModel
        {
            Resource = new ResourceModel
            {
                Namespace = "Showcase.Booking.Entities",
                TypeName = "Guest",
                FullTypeName = "global::Showcase.Booking.Entities.Guest",
                Segment = "guests",
                Capabilities = capabilities,
                BoundaryFullTypeName = "global::Showcase.Booking.BookingBoundary",
                BoundaryName = "Booking",
                IdType = "System.Guid",
            },
            IsSoftDelete = softDelete,
            Properties = ImmutableArray.Create(
                new ResourcePropertyInfo { Name = "Id", TypeName = "Guid", IsPrimaryKey = true },
                new ResourcePropertyInfo { Name = "Name", TypeName = "string", IsRequired = true },
                new ResourcePropertyInfo { Name = "Email", TypeName = "string", IsRequired = true },
                new ResourcePropertyInfo { Name = "OwnerId", TypeName = "string", IsOwnership = true },
                new ResourcePropertyInfo { Name = "IsDeleted", TypeName = "bool", IsSoftDelete = true },
                new ResourcePropertyInfo { Name = "CreatedAt", TypeName = "DateTimeOffset", IsAudit = true }),
        });
}
