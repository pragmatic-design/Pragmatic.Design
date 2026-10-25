using Pragmatic.Actions.Mutation;

namespace PragmaticEntities.Tests;

/// <summary>A delete of a room type: its soft delete cascades to its beds, marked with its instant.</summary>
[Mutation(Mode = MutationMode.Delete)]
public partial class CloseRoomTypeMutation : Mutation<RoomType>
{
    public required Guid Id { get; init; }
}
