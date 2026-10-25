using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Mutation;

namespace PragmaticEntities.Tests;

/// <summary>An update: the invoker loads the entity by key (404 when the caller cannot read it), applies the change, saves.</summary>
[Mutation(Mode = MutationMode.Update)]
public partial class RenameAmenityMutation : Mutation<Amenity>
{
    public required Guid Id { get; init; }

    public string? Name { get; init; }
}
