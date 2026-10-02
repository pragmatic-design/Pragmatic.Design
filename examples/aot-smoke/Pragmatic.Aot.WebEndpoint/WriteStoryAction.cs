using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;

namespace Pragmatic.Aot.WebEndpoint;

/// <summary>What the endpoint answers with.</summary>
public sealed class StoryDto
{
    public required string Title { get; init; }
    public int Slots { get; init; }
}

/// <summary>
///     An ordinary action: two body properties, one of them <c>required</c>, and a DTO back.
/// </summary>
/// <remarks>
///     Deliberately unremarkable. If the plainest possible endpoint does not survive an AOT publish,
///     nothing more elaborate will, and the smoke should say so on the simplest case rather than leave
///     the reader wondering which feature broke it.
/// </remarks>
[DomainAction]
[Endpoint(HttpVerb.Post, "api/stories")]
public partial class WriteStoryAction : DomainAction<StoryDto>
{
    public required string Title { get; init; }

    public int Slots { get; init; }

    public override Task<Result<StoryDto, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult<Result<StoryDto, IError>>(new StoryDto { Title = Title, Slots = Slots });
}
