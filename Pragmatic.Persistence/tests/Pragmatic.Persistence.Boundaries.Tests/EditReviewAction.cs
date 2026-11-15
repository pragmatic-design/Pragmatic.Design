using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authoring;
using Pragmatic.Persistence.Entity;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     Edits a review, whose boundary is on the documents engine with an event outbox, and declares <see cref="ReviewEdited" /> (#736).
/// </summary>
[DomainAction]
[BelongsTo<ReviewsBoundary>]
[Raises<ReviewEdited>]
[LoadEntity<ProductReview>(nameof(Id))]
public partial class EditReviewAction : VoidDomainAction<NotFoundError>
{
    public required Guid Id { get; init; }

    public required string Text { get; init; }

    public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        _productReview.Text = Text;
        return Task.FromResult<VoidResult<IError>>(Success);
    }
}
