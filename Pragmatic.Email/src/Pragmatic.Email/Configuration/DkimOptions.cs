namespace Pragmatic.Email.Configuration;

/// <summary>
///     DKIM signing configuration.
/// </summary>
public sealed class DkimOptions
{
    /// <summary>Signing domain (e.g., "example.com").</summary>
    public required string Domain { get; set; }

    /// <summary>DKIM selector (e.g., "pragmatic", used in DNS lookup: pragmatic._domainkey.example.com).</summary>
    public required string Selector { get; set; }

    /// <summary>
    ///     RSA private key in PEM format.
    /// </summary>
    /// <remarks>
    ///     Do NOT store this value in <c>appsettings.json</c> committed to source control.
    ///     Load from environment variables, Azure Key Vault, AWS Secrets Manager, or a
    ///     secrets manager at startup and inject via <c>IConfiguration</c> / user secrets.
    /// </remarks>
    public required string PrivateKeyPem { get; set; }
}
