using Conformance.Sales.Entities;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;

namespace Conformance.Sales.Actions;

/// <summary>
///     The names of the labels a list of keys names — the rows read by <c>[LoadEntities]</c>, which the
///     operation only has to read.
/// </summary>
/// <remarks>
///     Exists to pin the list load on a real database: the rows in the order of the keys, a key given twice
///     once, one 404 naming every key that names nothing, and one query for all of them — what a hand-written
///     <c>FindAsync</c> and a comparison did before, and where a missing key got forgotten.
/// </remarks>
[DomainAction]
[AllowAnonymous]
[LoadEntities<Label>(nameof(LabelIds))]
[Endpoint(HttpVerb.Post, "api/labels/names")]
public partial class NameLabelsAction : DomainAction<string[], IError>
{
    public required IReadOnlyList<Guid> LabelIds { get; init; }

    public override Task<Result<string[], IError>> Execute(CancellationToken ct = default)
        => Task.FromResult<Result<string[], IError>>(_labels.Select(label => label.Name).ToArray());
}
