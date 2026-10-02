namespace Pragmatic.Email.Configuration;

/// <summary>
///     Top-level email options.
/// </summary>
public sealed class EmailOptions
{
    /// <summary>Default sender address used when From is not specified on the message.</summary>
    public EmailAddress? DefaultFrom { get; set; }
}
