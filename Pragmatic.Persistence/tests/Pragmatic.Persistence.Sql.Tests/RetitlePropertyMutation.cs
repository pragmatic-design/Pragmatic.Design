using Pragmatic.Actions.Mutation;
using Pragmatic.Persistence.Entity;

namespace PragmaticEntities.Tests;

/// <summary>
///     An update that answers with a DTO reading a navigation (<c>[ReturnsDto]</c>): no <c>[EagerLoad]</c>, the invoker loads what the response
///     maps from, known at build.
/// </summary>
[Mutation(Mode = MutationMode.Update)]
[ReturnsDto<PropertyRoomsDto>]
public partial class RetitlePropertyMutation : Mutation<Property>
{
    public required Guid Id { get; init; }

    public string? Name { get; init; }
}
