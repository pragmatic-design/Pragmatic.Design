using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Result;

namespace PragmaticEntities.Tests;

/// <summary>A read by key through <c>[LoadEntity]</c>: the invoker loads the review, inside the caller's filters, before the action runs.</summary>
[DomainAction]
[LoadEntity<Review>(nameof(Id))]
public partial class ReadReviewAction : DomainAction<string>
{
    public required Guid Id { get; init; }

    public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult(Result<string, IError>.Success(_review.Text));
}
