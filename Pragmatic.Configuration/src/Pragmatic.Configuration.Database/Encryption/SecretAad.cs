namespace Pragmatic.Configuration.Database.Encryption;

/// <summary>
///     Builds the associated-data (AAD) string that binds a secret's ciphertext to its identity
///     (tenant + key), so a ciphertext cannot be moved between secrets and still authenticate. Single source
///     of truth shared by the store (encrypt/decrypt) and the key-rotation re-encrypt pass.
/// </summary>
internal static class SecretAad
{
    public static string For(string key, string? tenantId) => $"{tenantId ?? string.Empty}:{key}";
}
