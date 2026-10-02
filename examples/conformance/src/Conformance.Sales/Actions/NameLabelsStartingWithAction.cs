using Conformance.Sales.Entities;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;

namespace Conformance.Sales.Actions;

/// <summary>
///     The names of the labels starting with a prefix — the rows read by a rule, and none of them a 404
///     (<c>RequireAny</c>).
/// </summary>
/// <remarks>
///     Without <c>RequireAny</c> no row is an empty list; with it, the operation says it needs at least one.
/// </remarks>
[DomainAction]
[AllowAnonymous]
[LoadEntities<Label>(Specification = nameof(LabelSpecifications.NamedStartingWith), RequireAny = true)]
[Endpoint(HttpVerb.Post, "api/labels/starting-with")]
public partial class NameLabelsStartingWithAction : DomainAction<string[], IError>
{
    public required string Prefix { get; init; }

    public override Task<Result<string[], IError>> Execute(CancellationToken ct = default)
        => Task.FromResult<Result<string[], IError>>(_labels.Select(label => label.Name).ToArray());
}
