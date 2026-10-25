using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Result;

namespace PragmaticEntities.Tests;

/// <summary>A read by key with a navigation, through <c>[LoadEntity(Include)]</c>: the invoker loads the property with its room types.</summary>
[DomainAction]
[LoadEntity<Property>(nameof(Id), Include = nameof(Property.RoomTypes))]
public partial class ReadRoomTypesAction : DomainAction<IReadOnlyList<string>>
{
    public required Guid Id { get; init; }

    public override Task<Result<IReadOnlyList<string>, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult(Result<IReadOnlyList<string>, IError>.Success(_property.RoomTypes.Select(t => t.Code).ToList()));
}
