using Pragmatic.Notifications;

namespace Pragmatic.Identity.Local.Services;

/// <summary>
///     A local account's address, for a notification addressed to the user (#743): the <see cref="LocalIdentity.Email" /> of the identity whose
///     external key is the user's id, as the account's <c>sub</c> claim carries it. Local accounts know no phone number and assign no role.
/// </summary>
/// <remarks>
///     Registered by the generator beside the generated <see cref="ILocalIdentityStore" />, so it exists exactly where there are local
///     accounts to read.
/// </remarks>
/// <param name="identities">The application's store of local identities.</param>
public sealed class LocalIdentityRecipientDirectory(ILocalIdentityStore identities) : IRecipientDirectory
{
    /// <inheritdoc />
    public async Task<RecipientContact?> FindUserAsync(string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        return await identities.FindByExternalKeyAsync(userId, ct).ConfigureAwait(false) is { Email: { Length: > 0 } email }
            ? new RecipientContact(email, null)
            : null;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<string> UsersInRoleAsync(string role, string? tenantId, CancellationToken ct = default)
        => AsyncEnumerable.Empty<string>();
}
