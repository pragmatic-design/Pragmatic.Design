using Pragmatic.Actions.Mutation;

namespace PragmaticEntities.Tests;

/// <summary>A room type as a property's mutation writes it: matched by key, created when the key is new.</summary>
[Mutation(Mode = MutationMode.Update)]
public partial class WriteRoomTypeMutation : Mutation<RoomType>
{
    public Guid Id { get; init; }

    public string Code { get; init; } = "";
}
