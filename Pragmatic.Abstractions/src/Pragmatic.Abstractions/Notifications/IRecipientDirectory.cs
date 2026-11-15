namespace Pragmatic.Notifications;

/// <summary>
///     Where a notification addressed to a user or a role finds who to send it to: a user's addresses, a role's members. The modules that
///     know them implement it (local accounts their users, Authorization.Management its role assignments), and the notifications' recipient
///     resolver asks every one registered, in turn.
/// </summary>
/// <remarks>
///     <para>
///         In Abstractions because the modules that answer do not reference Notifications, and Notifications references none of them.
///     </para>
///     <para>
///         Registered as several (<c>[Service&lt;IRecipientDirectory&gt;(Multiple = true)]</c>). A directory answers what it knows and
///         nothing else: <see langword="null" /> for a user it does not hold, no members for a role it does not assign. A recipient no
///         directory answers is named in the notification's result as unreached, not dropped in silence.
///     </para>
/// </remarks>
public interface IRecipientDirectory
{
    /// <summary>The addresses of the user <paramref name="userId" /> (the caller's id, as <c>ICurrentUser.Id</c> carries it); null when unknown here.</summary>
    Task<RecipientContact?> FindUserAsync(string userId, CancellationToken ct = default);

    /// <summary>The ids of the users who hold <paramref name="role" /> now, in <paramref name="tenantId" /> or globally; none when unknown here.</summary>
    IAsyncEnumerable<string> UsersInRoleAsync(string role, string? tenantId, CancellationToken ct = default);
}
