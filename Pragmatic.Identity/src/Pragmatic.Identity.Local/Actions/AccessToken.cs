namespace Pragmatic.Identity.Local.Actions;

/// <summary>
///     A signed-in session: the bearer token to send on every request, and when it stops working.
/// </summary>
public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);
