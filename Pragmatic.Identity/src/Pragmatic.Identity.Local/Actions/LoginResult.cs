namespace Pragmatic.Identity.Local.Actions;

/// <summary>Result of a successful login.</summary>
public sealed record LoginResult(string ExternalIdentityKey, DateTimeOffset AuthenticatedAt);
