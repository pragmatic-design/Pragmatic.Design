namespace Pragmatic.Email;

/// <summary>
///     Public API for sending emails. Runs middleware pipeline then delegates to the transport.
/// </summary>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Singleton)]
public interface IEmailSender
{
    Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default);
}
