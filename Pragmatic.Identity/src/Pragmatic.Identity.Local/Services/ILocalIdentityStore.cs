namespace Pragmatic.Identity.Local.Services;

/// <summary>
///     Store for local identity records. Abstracts persistence details
///     (OwnsOne vs FK) from the actions.
/// </summary>
/// <remarks>
///     Every email a store receives is already in its stored form, <see cref="LocalIdentity.NormalizeEmail" />:
///     the actions normalize before they ask. A store compares it as given.
/// </remarks>
public interface ILocalIdentityStore
{
    /// <summary>Finds a local identity by email.</summary>
    ValueTask<LocalIdentity?> FindByEmailAsync(string email, CancellationToken ct = default);

    /// <summary>Finds a local identity by external identity key.</summary>
    ValueTask<LocalIdentity?> FindByExternalKeyAsync(string externalKey, CancellationToken ct = default);

    /// <summary>Creates a new local identity.</summary>
    ValueTask<LocalIdentity> CreateAsync(LocalIdentity identity, CancellationToken ct = default);

    /// <summary>Updates an existing local identity.</summary>
    ValueTask UpdateAsync(LocalIdentity identity, CancellationToken ct = default);

    /// <summary>Checks if an email is already in use.</summary>
    ValueTask<bool> EmailExistsAsync(string email, CancellationToken ct = default);
}
