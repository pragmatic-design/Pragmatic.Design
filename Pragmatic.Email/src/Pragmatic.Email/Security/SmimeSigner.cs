using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Pragmatic.Email.Security;

/// <summary>
///     S/MIME signer using CMS/PKCS#7. Generates detached signatures for email content.
/// </summary>
public sealed class SmimeSigner(SmimeOptions options)
{
    /// <summary>
    ///     Signs the MIME body content and returns the CMS signature bytes.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <see cref="SmimeOptions.SigningCertificate"/> does not have an associated private key.
    /// </exception>
    public byte[] Sign(string mimeContent)
    {
        if (!options.SigningCertificate.HasPrivateKey)
            throw new InvalidOperationException(
                "S/MIME signing requires a certificate with a private key. " +
                "Ensure SmimeOptions.SigningCertificate was loaded with its private key (e.g. from a .pfx file).");

        var contentBytes = Encoding.UTF8.GetBytes(mimeContent);
        var contentInfo = new ContentInfo(contentBytes);
        var signedCms = new SignedCms(contentInfo, detached: true);

        var signer = new CmsSigner(options.SigningCertificate)
        {
            IncludeOption = options.IncludeCertificate
                ? X509IncludeOption.WholeChain
                : X509IncludeOption.None,

            // Pinned so it always agrees with the micalg=sha-256 parameter the multipart/signed
            // container advertises; a mismatch there makes verifiers reject the signature.
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1", "SHA256"),
        };

        signedCms.ComputeSignature(signer);
        return signedCms.Encode();
    }
}
