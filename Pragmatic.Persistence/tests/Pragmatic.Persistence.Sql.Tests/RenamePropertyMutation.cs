using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Mutation;

namespace PragmaticEntities.Tests;

/// <summary>An update that loads a navigation with the entity (<c>[EagerLoad]</c>): the invoker reads the property by key with its room types.</summary>
[Mutation(Mode = MutationMode.Update)]
[EagerLoad(nameof(Property.RoomTypes))]
public partial class RenamePropertyMutation : Mutation<Property>
{
    public required Guid Id { get; init; }

    public string? Name { get; init; }
}
