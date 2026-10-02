using Pragmatic.Notifications;
using Pragmatic.Notifications.Pipeline;
using Pragmatic.Notifications.Preferences;

namespace Showcase.Booking.Infrastructure.Notifications;

/// <summary>
///     Resolves notification recipients for the Showcase: turns a guest id into the guest's e-mail
///     address, which is what <see cref="NotificationRecipient.User"/> targets.
/// </summary>
/// <remarks>
///     The built-in resolver only understands direct addresses — it cannot know where a user id lives.
///     Supplying this is the intended extension point, and without it every notification addressed to a
///     user resolves to nothing and is reported as failed. Registered scoped, so it can use the
///     repository.
/// </remarks>
public sealed class GuestRecipientResolver(
    IRepository<Guest> guests,
    INotificationPreferenceProvider preferences,
    IQueryFilterToggle filterToggle)
    : IRecipientResolver
{
    public async Task<IReadOnlyList<ResolvedRecipient>> ResolveAsync(
        NotificationRecipient recipient,
        NotificationAudience audience,
        CancellationToken ct = default)
    {
        var results = new List<ResolvedRecipient>();

        if (recipient.EmailAddress is not null)
            results.Add(await ResolveAddressAsync(recipient.EmailAddress, null, ct).ConfigureAwait(false));

        var guestIds = new List<Guid>();
        if (recipient.UserId is not null && Guid.TryParse(recipient.UserId, out var single))
            guestIds.Add(single);

        if (recipient.UserIds is { Count: > 0 })
        {
            foreach (var id in recipient.UserIds)
            {
                if (Guid.TryParse(id, out var parsed))
                    guestIds.Add(parsed);
            }
        }

        if (guestIds.Count > 0)
        {
            // Admin, not Background: ownership and data-scope filters must be bypassed because the
            // notification is sent on the system's behalf and there is no acting user in the delivery
            // worker — but the TENANT filter stays on, so a notification can never resolve a guest
            // belonging to another tenant. The worker restores the enqueuing tenant onto this scope.
            using var elevated = filterToggle.UseMode(FilterMode.Admin);

            foreach (var guestId in guestIds)
            {
                var guest = await guests.GetByIdAsync(guestId, ct).ConfigureAwait(false);
                if (guest is null)
                    continue;

                results.Add(await ResolveAddressAsync(guest.Email, guest.PreferredLanguage, ct).ConfigureAwait(false));
            }
        }

        return results;
    }

    private async Task<ResolvedRecipient> ResolveAddressAsync(string address, string? locale, CancellationToken ct)
    {
        var prefs = await preferences.GetPreferencesAsync(address, ct).ConfigureAwait(false);
        return new ResolvedRecipient(address, NotificationChannel.Email, locale ?? prefs?.Locale, null, prefs);
    }
}
