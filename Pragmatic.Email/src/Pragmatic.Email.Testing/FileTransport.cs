using Pragmatic.Email.Mime;
using Pragmatic.Email.Transport;

namespace Pragmatic.Email.Testing;

/// <summary>
///     File transport that writes .eml files for development and debugging.
/// </summary>
public sealed class FileTransport : IEmailTransport
{
    private readonly string _directory;

    public FileTransport(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public string Name => "File";

    public async Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var mime = MimeWriter.Write(message);

        // MessageId is caller-settable and has no minimum length, so slicing it blindly threw
        // ArgumentOutOfRangeException on anything shorter than 16 characters. Invalid path
        // characters are stripped for the same reason: the id ends up in a file name.
        var idPart = message.MessageId.Length > 16 ? message.MessageId[..16] : message.MessageId;
        foreach (var invalid in Path.GetInvalidFileNameChars())
            idPart = idPart.Replace(invalid, '_');

        var fileName = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{idPart}.eml";
        var filePath = Path.Combine(_directory, fileName);

        await File.WriteAllTextAsync(filePath, mime, ct).ConfigureAwait(false);
        return EmailResult.Succeeded(message.MessageId);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
