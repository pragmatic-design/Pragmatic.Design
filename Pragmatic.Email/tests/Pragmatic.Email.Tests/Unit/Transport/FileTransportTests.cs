using Pragmatic.Testing.Assertions;
using Pragmatic.Email.Testing;

namespace Pragmatic.Email.Tests.Unit.Transport;

public sealed class FileTransportTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileTransport _transport;

    public FileTransportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pragmatic-email-test-{Guid.NewGuid():N}");
        _transport = new FileTransport(_tempDir);
    }

    [Fact]
    public async Task SendAsync_WritesEmlFile()
    {
        var message = new EmailMessage
        {
            From = new EmailAddress("sender@example.com"),
            To = [new EmailAddress("recipient@example.com")],
            Subject = "File Test",
            TextBody = "Hello from file transport",
        };

        var result = await _transport.SendAsync(message);

        result.Success.Should().BeTrue();
        Directory.GetFiles(_tempDir, "*.eml").Should().HaveCount(1);
    }

    [Fact]
    public async Task SendAsync_FileContainsMimeHeaders()
    {
        var message = new EmailMessage
        {
            From = new EmailAddress("sender@example.com", "Sender"),
            To = [new EmailAddress("recipient@example.com")],
            Subject = "Header Test",
            TextBody = "Body content",
        };

        await _transport.SendAsync(message);

        var file = Directory.GetFiles(_tempDir, "*.eml").Single();
        var content = await File.ReadAllTextAsync(file);

        content.Should().Contain("From: Sender <sender@example.com>");
        content.Should().Contain("Subject: Header Test");
        content.Should().Contain("MIME-Version: 1.0");
        content.Should().Contain("Body content");
    }

    [Fact]
    public async Task SendAsync_MultipleEmails_CreatesMultipleFiles()
    {
        for (var i = 0; i < 3; i++)
        {
            await _transport.SendAsync(new EmailMessage
            {
                From = new EmailAddress("sender@example.com"),
                To = [new EmailAddress("recipient@example.com")],
                Subject = $"Email {i}",
                TextBody = "Body",
            });
        }

        Directory.GetFiles(_tempDir, "*.eml").Should().HaveCount(3);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task SendAsync_ShortMessageId_DoesNotThrow()
    {
        // Regression: the file name sliced MessageId[..16] unconditionally, so any caller-set id
        // shorter than 16 characters crashed the transport with ArgumentOutOfRangeException.
        var directory = Path.Combine(Path.GetTempPath(), "pragmatic-email-" + Guid.NewGuid().ToString("N"));
        var transport = new FileTransport(directory);

        try
        {
            var message = new EmailMessage
            {
                From = new EmailAddress("sender@example.com"),
                To = [new EmailAddress("recipient@example.com")],
                Subject = "Short id",
                TextBody = "Body",
                MessageId = "short",
            };

            var result = await transport.SendAsync(message);

            result.Success.Should().BeTrue();
            Directory.GetFiles(directory, "*.eml").Should().ContainSingle();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
