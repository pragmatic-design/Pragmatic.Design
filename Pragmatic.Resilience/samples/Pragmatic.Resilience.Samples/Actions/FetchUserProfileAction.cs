using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Resilience.Attributes;
using Pragmatic.Result;

namespace Pragmatic.Resilience.Samples.Actions;

/// <summary>
///     Simulates fetching a user profile from an external API.
///     [ResiliencePolicy("external-api")] wraps execution with retry + timeout.
/// </summary>
[DomainAction]
[ResiliencePolicy("external-api")]
public partial class FetchUserProfileAction : DomainAction<string>
{
    public required string UserId { get; init; }

    public override async Task<Result<string, IError>> Execute(CancellationToken ct = default)
    {
        // Simulate external API call
        await Task.Delay(50, ct).ConfigureAwait(false);
        return Result<string, IError>.Success($"User profile for {UserId}");
    }
}
