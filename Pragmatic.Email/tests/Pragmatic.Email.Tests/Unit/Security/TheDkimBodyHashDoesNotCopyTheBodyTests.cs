using System.Security.Cryptography;
using System.Text;
using Pragmatic.Testing.Assertions;
using Pragmatic.Email.Configuration;
using Pragmatic.Email.Security;

namespace Pragmatic.Email.Tests.Unit.Security;

/// <summary>
///     <see cref="DkimSigner.Sign" /> hashes the body as it canonicalizes it, line by line, instead of
///     building the canonical body as a string and then as UTF-8 bytes.
/// </summary>
/// <remarks>
///     The body hash is checked against <see cref="DkimVerifier" />, which canonicalizes on its own from
///     RFC 6376: the streaming hash has to produce the same <c>bh=</c> the whole-string version did,
///     on the bodies where canonicalization does something. Measured on a 10,485,774-character body:
///     119,131,968 bytes allocated per signature before the change, 7,472 after.
/// </remarks>
public sealed class TheDkimBodyHashDoesNotCopyTheBodyTests : IDisposable
{
    private const string HeaderBlock = "From: sender@example.com\r\nTo: recipient@example.com\r\nSubject: Large\r\n\r\n";

    private readonly RSA _key = RSA.Create(2048);
    private readonly DkimSigner _signer;

    public TheDkimBodyHashDoesNotCopyTheBodyTests()
        => _signer = new DkimSigner(new DkimOptions
        {
            Domain = "example.com",
            Selector = "test",
            PrivateKeyPem = _key.ExportRSAPrivateKeyPem(),
        });

    public void Dispose()
    {
        _signer.Dispose();
        _key.Dispose();
    }

    [Fact]
    public void Sign_TenMegabyteBody_AllocatesFarLessThanTheBody()
    {
        // 76-character lines, as a base64 attachment is written.
        var line = new string('A', 76) + "\r\n";
        var body = new StringBuilder(10 * 1024 * 1024 + line.Length);
        while (body.Length < 10 * 1024 * 1024)
            body.Append(line);
        var mime = HeaderBlock + body;

        _signer.Sign(mime);
        var before = GC.GetAllocatedBytesForCurrentThread();
        _signer.Sign(mime);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().BeLessThan(256 * 1024,
            $"the body is {body.Length:N0} characters and should be hashed without being copied");
    }

    [Theory]
    [InlineData("Hello DKIM")]
    [InlineData("Hello DKIM\r\n")]
    [InlineData("")]
    [InlineData("\r\n\r\n\r\n")]
    [InlineData("Hello   \r\nWorld \t \r\n")]
    [InlineData("Tabs\tand   spaces \t inside\r\n")]
    [InlineData("First\r\n\r\n\r\nAfter blank lines\r\n\r\n\r\n")]
    [InlineData(" leading space kept as one\r\n")]
    [InlineData("Caffè, naïve, 日本語 and 🎉\r\n")]
    [InlineData("no final line break")]
    public void Sign_Body_ProducesTheBodyHashAnIndependentVerifierComputes(string body)
    {
        var mime = HeaderBlock + body;

        var signed = $"DKIM-Signature: {_signer.Sign(mime)}\r\n{mime}";

        DkimVerifier.Verify(signed, _key).IsValid.Should().BeTrue();
    }
}
