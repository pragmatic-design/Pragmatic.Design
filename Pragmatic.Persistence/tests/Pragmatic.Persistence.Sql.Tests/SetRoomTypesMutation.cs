using Pragmatic.Actions.Mutation;

namespace PragmaticEntities.Tests;

/// <summary>Rewrites a property's room types: the set sent is the set that remains (a keyed merge, the Sync strategy).</summary>
[Mutation(Mode = MutationMode.Update)]
public partial class SetRoomTypesMutation : Mutation<Property>
{
    public required Guid Id { get; init; }

    public required List<WriteRoomTypeMutation> RoomTypes { get; init; }
}
