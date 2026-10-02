namespace Pragmatic.Configuration;

/// <summary>
///     A configuration value that is a <b>reference</b> to a secret rather than the secret itself:
///     <c>secret://{key}</c>. The reference is what lives in the configuration store (and therefore in
///     any distributed fabric backing it, e.g. the Agent KV / gossip / disk); the secret material never
///     leaves the <see cref="ISecretStore"/>. It is resolved locally, at read time, by the configuration
///     resolver.
/// </summary>
/// <remarks>
///     This is the seam that lets an enterprise secret store (Key Vault / Vault / cloud Secrets Manager)
///     be the single source of truth for secrets while a config value merely points at it — so a
///     sensitive value can be distributed without ever landing in plaintext in the coordination fabric.
/// </remarks>
public static class SecretReference
{
    /// <summary>The reference scheme prefix.</summary>
    public const string Scheme = "secret://";

    /// <summary>
    ///     Returns <c>true</c> and the referenced secret key when <paramref name="value"/> is a
    ///     <c>secret://{key}</c> reference with a non-empty key; otherwise <c>false</c>.
    /// </summary>
    public static bool TryParse(string? value, out string secretKey)
    {
        if (value is not null
            && value.StartsWith(Scheme, StringComparison.Ordinal)
            && value.Length > Scheme.Length)
        {
            secretKey = value[Scheme.Length..];
            return true;
        }

        secretKey = string.Empty;
        return false;
    }

    /// <summary>Whether <paramref name="value"/> is a secret reference.</summary>
    public static bool IsReference(string? value) => TryParse(value, out _);

    /// <summary>Builds a <c>secret://{key}</c> reference for storing in configuration.</summary>
    public static string Create(string secretKey) => Scheme + secretKey;
}
