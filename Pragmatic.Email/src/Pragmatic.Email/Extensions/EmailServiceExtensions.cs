using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Email.Configuration;
using Pragmatic.Email.Middleware;
using Pragmatic.Email.Transport;

namespace Pragmatic.Email.Extensions;

/// <summary>
///     DI registration for Pragmatic.Email.
/// </summary>
public static class EmailServiceExtensions
{
    /// <summary>Adds the email system with builder configuration.</summary>
    public static IServiceCollection AddPragmaticEmail(
        this IServiceCollection services,
        Action<EmailBuilder>? configure = null)
    {
        var builder = new EmailBuilder(services);
        configure?.Invoke(builder);

        // Applies EmailOptions.DefaultFrom to messages without a sender. Always registered: it is a
        // no-op when DefaultFrom is not configured, and without it the option is never read at all.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IEmailMiddleware, DefaultFromMiddleware>());

        // Defaults (TryAdd to allow user overrides)
        services.TryAddSingleton<IEmailTransport, NullTransport>();
        services.TryAddSingleton<EmailPipeline>();
        services.TryAddSingleton<IEmailSender, EmailSender>();

        return services;
    }
}
