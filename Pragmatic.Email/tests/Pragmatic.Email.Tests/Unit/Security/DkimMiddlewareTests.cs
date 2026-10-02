using System.Security.Cryptography;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Options;
using Pragmatic.Email.Configuration;
using Pragmatic.Email.Middleware;
using Pragmatic.Email.Security;

namespace Pragmatic.Email.Tests.Unit.Security;

public sealed class DkimMiddlewareTests
{
    private static DkimMiddleware CreateMiddleware()
    {
        using var rsa = RSA.Create(2048);
        var options = new DkimOptions
        {
            Domain = "example.com",
            Selector = "test",
            PrivateKeyPem = rsa.ExportRSAPrivateKeyPem(),
        };

        return new DkimMiddleware(Options.Create(options));
    }

    private static EmailMessage CreateMessage() => new()
    {
        From = new EmailAddress("sender@example.com", "Sender"),
        To = [new EmailAddress("recipient@example.com")],
        Subject = "DKIM Pipeline Test",
        TextBody = "Body",
    };

    [Fact]
    public void Order_IsAfterContentTransformations()
    {
        CreateMiddleware().Order.Should().Be(100);
    }

    [Fact]
    public async Task ProcessAsync_AddsDkimSignatureHeader()
    {
        var middleware = CreateMiddleware();

        var result = await middleware.ProcessAsync(CreateMessage(), Identity, CancellationToken.None);

        result.Headers.Should().ContainKey("DKIM-Signature");
        result.Headers["DKIM-Signature"].Should().Contain("d=example.com");
    }

    [Fact]
    public async Task ProcessAsync_PreservesExistingHeaders()
    {
        var middleware = CreateMiddleware();
        var message = CreateMessage() with
        {
            Headers = new Dictionary<string, string> { ["X-Existing"] = "value" },
        };

        var result = await middleware.ProcessAsync(message, Identity, CancellationToken.None);

        result.Headers.Should().ContainKey("X-Existing");
        result.Headers.Should().ContainKey("DKIM-Signature");
    }

    [Fact]
    public async Task ProcessAsync_DoesNotMutateInputMessage()
    {
        var middleware = CreateMiddleware();
        var message = CreateMessage();

        await middleware.ProcessAsync(message, Identity, CancellationToken.None);

        message.Headers.Should().NotContainKey("DKIM-Signature");
    }

    [Fact]
    public async Task ProcessAsync_CallsNextWithSignedMessage()
    {
        var middleware = CreateMiddleware();
        EmailMessage? passedToNext = null;

        await middleware.ProcessAsync(CreateMessage(), m =>
        {
            passedToNext = m;
            return Task.FromResult(m);
        }, CancellationToken.None);

        passedToNext.Should().NotBeNull();
        passedToNext!.Headers.Should().ContainKey("DKIM-Signature");
    }

    private static Task<EmailMessage> Identity(EmailMessage m) => Task.FromResult(m);
}
