using Pragmatic.Composition;
using Pragmatic.Email.Configuration;
using Pragmatic.Email.Extensions;

namespace Pragmatic.Email;

/// <summary>
///     Extension methods for configuring Email on <see cref="IPragmaticBuilder"/>.
/// </summary>
public static class PragmaticBuilderEmailExtensions
{
    /// <summary>
    ///     Configures the email system for the application.
    /// </summary>
    public static IPragmaticBuilder UseEmail(
        this IPragmaticBuilder builder,
        Action<EmailBuilder>? configure = null)
    {
        builder.Services.AddPragmaticEmail(configure);
        return builder;
    }
}
