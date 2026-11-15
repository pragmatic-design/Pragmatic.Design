using Pragmatic.MultiTenancy;
using Pragmatic.Notifications.Preferences;

namespace Pragmatic.Notifications.Pipeline;

/// <summary>
///     Default recipient resolver: direct addresses as they are, a user through the <see cref="IRecipientDirectory" />s registered (local
///     accounts know their users' e-mail), a role through them too (Authorization.Management knows who holds it), each member once.
/// </summary>
/// <remarks>
///     What no directory answers is <b>not dropped</b>: it is named in <see cref="RecipientResolution.Unreached" />, and the notification's
///     result names it, so a send to nobody is visible to whoever sent it (#743). A tenant recipient is not resolved by any directory and is
///     reported the same way.
/// </remarks>
internal sealed class DefaultRecipientResolver(
    INotificationPreferenceProvider preferenceProvider,
    IEnumerable<IRecipientDirectory> directories,
    ITenantContext? tenant = null)
    : IRecipientResolver
{
    private readonly IRecipientDirectory[] _directories = [.. directories];

    public async Task<RecipientResolution> ResolveAsync(
        NotificationRecipient recipient,
        NotificationAudience audience,
        CancellationToken ct = default)
    {
        var results = new List<ResolvedRecipient>();
        var unreached = new List<string>();

        if (recipient.EmailAddress is not null)
            results.Add(await AddressAsync(recipient.EmailAddress, NotificationChannel.Email, ct).ConfigureAwait(false));

        if (recipient.WebhookUrl is not null)
            results.Add(await AddressAsync(recipient.WebhookUrl, NotificationChannel.Webhook, ct).ConfigureAwait(false));

        // Preferences are loaded for every channel, not just email: an opt-out or a muted category applies to the recipient, and leaving
        // them null here let SMS ignore do-not-disturb.
        if (recipient.PhoneNumber is not null)
            results.Add(await AddressAsync(recipient.PhoneNumber, NotificationChannel.Sms, ct).ConfigureAwait(false));

        // Each user once, whether named directly, among several, or as a member of the role.
        var users = new HashSet<string>(StringComparer.Ordinal);
        if (recipient.UserId is not null && users.Add(recipient.UserId))
            await UserAsync(recipient.UserId, $"user '{recipient.UserId}'", results, unreached, ct).ConfigureAwait(false);

        if (recipient.UserIds is { Count: > 0 })
        {
            foreach (var userId in recipient.UserIds)
            {
                if (users.Add(userId))
                    await UserAsync(userId, $"user '{userId}'", results, unreached, ct).ConfigureAwait(false);
            }
        }

        if (recipient.RoleName is not null)
        {
            var members = 0;
            foreach (var directory in _directories)
            {
                await foreach (var userId in directory.UsersInRoleAsync(recipient.RoleName, tenant?.TenantId, ct).ConfigureAwait(false))
                {
                    members++;
                    if (users.Add(userId))
                        await UserAsync(userId, $"user '{userId}' (role '{recipient.RoleName}')", results, unreached, ct).ConfigureAwait(false);
                }
            }

            if (members == 0)
                unreached.Add($"role '{recipient.RoleName}'");
        }

        // No directory resolves a tenant to the people who administer it.
        if (recipient.TenantId is not null)
            unreached.Add($"tenant '{recipient.TenantId}'");

        return new RecipientResolution(results, unreached);
    }

    /// <summary>The user's addresses, from the first directory that knows them; the user named as unreached when none does.</summary>
    private async Task UserAsync(string userId, string named, List<ResolvedRecipient> results, List<string> unreached, CancellationToken ct)
    {
        RecipientContact? contact = null;
        foreach (var directory in _directories)
        {
            contact = await directory.FindUserAsync(userId, ct).ConfigureAwait(false);
            if (contact is not null)
                break;
        }

        var before = results.Count;
        if (contact?.Email is { Length: > 0 } email)
            results.Add(await AddressAsync(email, NotificationChannel.Email, ct).ConfigureAwait(false));
        if (contact?.Phone is { Length: > 0 } phone)
            results.Add(await AddressAsync(phone, NotificationChannel.Sms, ct).ConfigureAwait(false));
        if (results.Count == before)
            unreached.Add(named);
    }

    private async Task<ResolvedRecipient> AddressAsync(string address, NotificationChannel channel, CancellationToken ct)
    {
        var prefs = await preferenceProvider.GetPreferencesAsync(address, ct).ConfigureAwait(false);
        return new ResolvedRecipient(address, channel, prefs?.Locale, null, prefs);
    }
}
