using Conformance.Sales.Entities;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;

namespace Conformance.Sales.Actions;

/// <summary>
///     The key of the label with a name — the row read by a rule, <c>[LoadEntity(Specification = …)]</c>,
///     instead of by a key.
/// </summary>
/// <remarks>
///     Pins the single load by rule on a real database: <see cref="LabelSpecifications.Named" />'s
///     <c>name</c> bound to <see cref="Name" /> by name, and a rule that matches nothing answered 404 before
///     the body runs.
/// </remarks>
[DomainAction]
[AllowAnonymous]
[LoadEntity<Label>(Specification = nameof(LabelSpecifications.Named))]
[Endpoint(HttpVerb.Post, "api/labels/find")]
public partial class FindLabelByNameAction : DomainAction<Guid, IError>
{
    public required string Name { get; init; }

    public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult<Result<Guid, IError>>(_label.PersistenceId);
}
