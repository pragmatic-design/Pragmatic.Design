namespace Pragmatic.Gateway;

/// <summary>
///     TLS certificate configuration for HTTPS listeners.
///     <para>
///         <b>Security note</b>: <see cref="CertificatePassword" /> is a plain string that may appear
///         in diagnostic dumps, config snapshots, and process memory. Prefer storing it via a secrets
///         manager (e.g. Azure Key Vault, HashiCorp Vault, environment variable) and never commit it
///         to source control.
///     </para>
/// </summary>
public sealed class TlsOptions
{
    /// <summary>Path to the PEM or PKCS#12 certificate file.</summary>
    public string? CertificatePath { get; set; }

    /// <summary>
    ///     Password for a PKCS#12 (.pfx) certificate.
    ///     <b>Do not store in appsettings.json.</b> Use a secrets manager or env var.
    /// </summary>
    public string? CertificatePassword { get; set; }

    /// <summary>Path to the PEM private-key file (PEM pair only; omit for PKCS#12).</summary>
    public string? KeyPath { get; set; }
}
