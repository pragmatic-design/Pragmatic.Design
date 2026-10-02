using System.Security.Claims;

namespace Pragmatic.Identity;

/// <summary>
///     A delegation read off the token's claims.
/// </summary>
/// <param name="subjectId">The subject, which is also <see cref="ICurrentUser.Id" />.</param>
/// <param name="actorId">The acting party, from <c>act_sub</c>.</param>
/// <param name="principal">The principal carrying the qualifying claims.</param>
internal sealed class ClaimsDelegationContext(string subjectId, string actorId, ClaimsPrincipal principal)
    : IDelegationContext
{
    /// <inheritdoc />
    public string SubjectId { get; } = subjectId;

    /// <inheritdoc />
    public string ActorId { get; } = actorId;

    /// <inheritdoc />
    public ActorKind ActorKind =>
        Enum.TryParse<ActorKind>(principal.FindFirst(DelegationClaims.ActorKind)?.Value, ignoreCase: true, out var kind)
            ? kind
            : ActorKind.Service;

    /// <inheritdoc />
    /// <remarks>
    ///     An unreadable or absent policy claim falls back to <see cref="DelegationPolicy.Intersection" />,
    ///     the narrowest. A malformed token must not widen authority.
    /// </remarks>
    public DelegationPolicy Policy =>
        Enum.TryParse<DelegationPolicy>(principal.FindFirst(DelegationClaims.Policy)?.Value, ignoreCase: true, out var policy)
            ? policy
            : DelegationPolicy.Intersection;

    /// <inheritdoc />
    public string? Purpose => principal.FindFirst(DelegationClaims.Purpose)?.Value;

    /// <inheritdoc />
    public string? GrantId => principal.FindFirst(DelegationClaims.GrantId)?.Value;

    /// <inheritdoc />
    public DateTimeOffset? ExpiresAt =>
        long.TryParse(principal.FindFirst("exp")?.Value, out var unix)
            ? DateTimeOffset.FromUnixTimeSeconds(unix)
            : null;

    /// <inheritdoc />
    public IReadOnlyList<string> Chain =>
        principal.FindFirst(DelegationClaims.Chain)?.Value is { Length: > 0 } chain
            ? chain.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
}
