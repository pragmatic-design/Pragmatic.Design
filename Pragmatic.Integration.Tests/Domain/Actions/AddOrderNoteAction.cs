using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;

namespace Pragmatic.Integration.Tests.Domain.Actions;

/// <summary>
///     Adds a note to an order.
/// </summary>
/// <remarks>
///     Here for what the other actions of this host do not have: a JSON body (<see cref="Text" />, which
///     no attribute places anywhere else), a header, and a query value that is <c>required</c>.
/// </remarks>
[DomainAction]
[Endpoint(Pragmatic.Endpoints.HttpVerb.Post, "/api/orders/{id}/notes")]
public partial class AddOrderNoteAction : VoidDomainAction
{
    /// <summary>The order the note is about.</summary>
    public required Guid Id { get; init; }

    /// <summary>The note.</summary>
    public required string Text { get; init; }

    /// <summary>How urgent the note is: a query value the binding refuses to go without.</summary>
    [FromQuery]
    public required int Priority { get; init; }

    /// <summary>Who wrote it, when the caller says.</summary>
    [FromHeader(Name = "X-Note-Source")]
    public string? Source { get; init; }

    /// <inheritdoc />
    public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
        => Task.FromResult(VoidResult<IError>.Success());
}
