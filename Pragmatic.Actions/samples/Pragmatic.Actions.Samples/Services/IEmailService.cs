namespace Pragmatic.Actions.Samples.Services;

/// <summary>
///     Sample service interface for sending emails.
/// </summary>
public interface IEmailService
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}
