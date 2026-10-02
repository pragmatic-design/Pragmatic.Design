using Conformance.Sales.Entities;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;

namespace Conformance.Sales.Actions;

/// <summary>
///     The names of two labels, each named by a key of its own — two <c>[LoadEntity]</c> of one entity.
/// </summary>
/// <remarks>
///     Exists to pin the merged read on a real database: the two keys are one query, each field is taken from
///     it, a key that names nothing is still a 404 naming it, and an optional key that is null is not asked.
/// </remarks>
[DomainAction]
[AllowAnonymous]
[LoadEntity<Label>(nameof(FirstId), FieldName = "_first")]
[LoadEntity<Label>(nameof(SecondId), FieldName = "_second")]
[Endpoint(HttpVerb.Post, "api/labels/pair")]
public partial class NameLabelPairAction : DomainAction<string[], IError>
{
    public required Guid FirstId { get; init; }

    public Guid? SecondId { get; init; }

    public override Task<Result<string[], IError>> Execute(CancellationToken ct = default)
    {
        string[] names = _second is null ? [_first.Name] : [_first.Name, _second.Name];
        return Task.FromResult<Result<string[], IError>>(names);
    }
}
