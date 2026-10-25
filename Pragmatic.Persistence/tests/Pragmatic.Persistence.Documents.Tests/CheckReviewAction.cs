using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Result;

namespace PragmaticDocuments.Tests;

/// <summary>An existence check through <c>[RequireExists]</c>: the invoker asks the repository whether the caller can see the review, and reads nothing.</summary>
[DomainAction]
[RequireExists<Review>(nameof(Id))]
public partial class CheckReviewAction : DomainAction<bool>
{
    public required Guid Id { get; init; }

    public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult(Result<bool, IError>.Success(true));
}
