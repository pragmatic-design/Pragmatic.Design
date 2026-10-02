namespace Pragmatic.Email.Configuration;

/// <summary>
///     SMTP authentication method.
/// </summary>
public enum SmtpAuthMethod
{
    /// <summary>Auto-detect: XOAUTH2 if token set, PLAIN if credentials set, none otherwise.</summary>
    Auto = 0,
    Plain = 1,
    Login = 2,
    XOAuth2 = 3,
}
