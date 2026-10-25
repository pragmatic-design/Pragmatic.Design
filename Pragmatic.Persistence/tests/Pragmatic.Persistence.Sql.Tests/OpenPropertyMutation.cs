using Pragmatic.Actions.Mutation;

namespace PragmaticEntities.Tests;

/// <summary>A create with children: the new property and its room types, saved as one graph.</summary>
[Mutation(Mode = MutationMode.Create)]
public partial class OpenPropertyMutation : Mutation<Property>
{
    public required string Name { get; init; }

    public required List<WriteRoomTypeMutation> RoomTypes { get; init; }
}
