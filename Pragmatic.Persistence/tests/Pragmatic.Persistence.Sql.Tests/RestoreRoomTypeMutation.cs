using Pragmatic.Actions.Mutation;

namespace PragmaticEntities.Tests;

/// <summary>A restore: the invoker reads the soft-deleted row by key with the entity's soft delete filter lifted, and no other.</summary>
[Mutation(Mode = MutationMode.Restore)]
public partial class RestoreRoomTypeMutation : Mutation<RoomType>
{
    public required Guid Id { get; init; }
}
