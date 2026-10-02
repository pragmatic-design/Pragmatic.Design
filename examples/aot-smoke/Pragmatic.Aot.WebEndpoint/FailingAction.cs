using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Aot.WebEndpoint;

/// <summary>
///     An action that fails, so the error path is published and exercised too.
/// </summary>
/// <remarks>
///     The error response is the one that runs when something has already gone wrong, and until
///     <c>ProblemDetails</c> was covered by a context of its own it was also the one that could not
///     serialize under AOT. A smoke that only ever takes the happy path would never have found that.
/// </remarks>
[DomainAction]
[Endpoint(HttpVerb.Get, "api/missing")]
public partial class FailingAction : DomainAction<StoryDto>
{
    public override Task<Result<StoryDto, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult<Result<StoryDto, IError>>(NotFoundError.Create("Story", "nope"));
}
