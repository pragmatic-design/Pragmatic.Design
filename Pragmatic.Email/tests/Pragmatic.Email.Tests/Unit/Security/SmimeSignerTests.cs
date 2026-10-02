using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Pragmatic.Testing.Assertions;
using Pragmatic.Email.Security;

namespace Pragmatic.Email.Tests.Unit.Security;

public sealed class SmimeSignerTests
{
    private static X509Certificate2 CreateCertWithPrivateKey()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Pragmatic Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static X509Certificate2 CreateCertWithoutPrivateKey()
    {
        using var full = CreateCertWithPrivateKey();
        // Re-import only the public part (no private key).
        return X509CertificateLoader.LoadCertificate(full.Export(X509ContentType.Cert));
    }

    [Fact]
    public void Sign_WithPrivateKey_ProducesVerifiableCms()
    {
        using var cert = CreateCertWithPrivateKey();
        using var options = new SmimeOptions { SigningCertificate = cert };
        var signer = new SmimeSigner(options);
        const string content = "MIME content to sign";

        var signature = signer.Sign(content);

        signature.Should().NotBeEmpty();

        // Re-decode and verify against the original detached content.
        var signedCms = new SignedCms(new ContentInfo(Encoding.UTF8.GetBytes(content)), detached: true);
        signedCms.Decode(signature);
        var verify = () => signedCms.CheckSignature(verifySignatureOnly: true);
        verify.Should().NotThrow();
    }

    [Fact]
    public void Sign_WithoutPrivateKey_ThrowsInvalidOperation()
    {
        using var cert = CreateCertWithoutPrivateKey();
        using var options = new SmimeOptions { SigningCertificate = cert };
        var signer = new SmimeSigner(options);

        var act = () => signer.Sign("content");

        act.Should().Throw<InvalidOperationException>().WithMessage("*private key*");
    }

    [Fact]
    public void Sign_DifferentContent_ProducesDifferentSignatures()
    {
        using var cert = CreateCertWithPrivateKey();
        using var options = new SmimeOptions { SigningCertificate = cert };
        var signer = new SmimeSigner(options);

        var first = signer.Sign("content A");
        var second = signer.Sign("content B");

        first.Should().NotBeEquivalentTo(second);
    }

    [Fact]
    public void Sign_WithoutIncludeCertificate_StillProducesSignature()
    {
        using var cert = CreateCertWithPrivateKey();
        using var options = new SmimeOptions { SigningCertificate = cert, IncludeCertificate = false };
        var signer = new SmimeSigner(options);

        var signature = signer.Sign("content");

        signature.Should().NotBeEmpty();
    }
}
