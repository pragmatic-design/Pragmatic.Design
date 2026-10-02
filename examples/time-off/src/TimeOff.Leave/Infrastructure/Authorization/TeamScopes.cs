namespace TimeOff.Leave.Infrastructure.Authorization;

/// <summary>
///     The role that says "manages this team", and the scope a leave request carries for it.
/// </summary>
/// <remarks>
///     A leave request is visible to whoever holds one of its scopes: its requester
///     (<c>user:{id}</c>, stamped at submission) and the manager of the team it was asked in
///     (<c>role:manages:{teamId}</c>). The manager's token carries the role, one per team they
///     manage, and a change of manager revokes the tokens of both managers — so the set in the token
///     is never stale for longer than a sign-in.
/// </remarks>
public static class TeamScopes
{
    /// <summary>The role a team's manager holds for it.</summary>
    public static string ManagerRoleOf(Guid teamId) => $"manages:{teamId:N}";

    /// <summary>The scope a request asked in <paramref name="teamId" /> carries.</summary>
    public static string ScopeOf(Guid teamId) => ScopeIdentifiers.ForRole(ManagerRoleOf(teamId));
}
