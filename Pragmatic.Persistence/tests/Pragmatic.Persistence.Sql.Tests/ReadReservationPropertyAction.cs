using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Result;

namespace PragmaticEntities.Tests;

/// <summary>
///     A read by key with a required reference, through <c>[LoadEntity(Include)]</c> (#496): the reservation with its property, the navigation
///     non-nullable, as the Showcase's <c>RoomType.Property</c> is.
/// </summary>
[DomainAction]
[LoadEntity<Reservation>(nameof(Id), Include = nameof(Reservation.Property))]
public partial class ReadReservationPropertyAction : DomainAction<string>
{
    public required Guid Id { get; init; }

    public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult(Result<string, IError>.Success(_reservation.Property.Name));
}
