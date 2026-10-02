using Conformance.Sales.Entities;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;

namespace Conformance.Sales.Actions;

/// <summary>
///     The names of the labels starting with a prefix, for a caller who may read labels — the load asks the
///     entity's read permission (<c>RequireReadPermission</c>).
/// </summary>
/// <remarks>
///     The operation itself is open (<c>[AllowAnonymous]</c>), so the only thing that can refuse a caller is
///     the preload: 401 for nobody signed in, 403 without <c>sales.label.read</c>, the names with it.
/// </remarks>
[DomainAction]
[AllowAnonymous]
[LoadEntities<Label>(Specification = nameof(LabelSpecifications.NamedStartingWith), RequireReadPermission = true)]
[Endpoint(HttpVerb.Post, "api/labels/starting-with/for-a-reader")]
public partial class NameLabelsForAReaderAction : DomainAction<string[], IError>
{
    public required string Prefix { get; init; }

    public override Task<Result<string[], IError>> Execute(CancellationToken ct = default)
        => Task.FromResult<Result<string[], IError>>(_labels.Select(label => label.Name).ToArray());
}
