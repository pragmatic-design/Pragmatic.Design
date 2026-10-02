using System.Security.Cryptography.X509Certificates;

namespace Pragmatic.Email.Security;

/// <summary>
///     S/MIME signing configuration.
/// </summary>
public sealed class SmimeOptions : IDisposable
{
    /// <summary>X.509 certificate with private key for signing.</summary>
    public required X509Certificate2 SigningCertificate { get; set; }

    /// <summary>Whether to include the signing certificate in the signature (default: true).</summary>
    public bool IncludeCertificate { get; set; } = true;

    /// <inheritdoc />
    public void Dispose() => SigningCertificate.Dispose();
}
