using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Result;

namespace PragmaticEntities.Tests;

/// <summary>A list read through <c>[LoadEntities]</c>: the invoker reads the distinct keys in one query, in the keys' order, or names every key missing.</summary>
[DomainAction]
[LoadEntities<Review>(nameof(Ids))]
public partial class ReadReviewsAction : DomainAction<IReadOnlyList<string>>
{
    public IReadOnlyList<Guid>? Ids { get; init; }

    public override Task<Result<IReadOnlyList<string>, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult(Result<IReadOnlyList<string>, IError>.Success(_reviews.Select(r => r.Text).ToList()));
}
