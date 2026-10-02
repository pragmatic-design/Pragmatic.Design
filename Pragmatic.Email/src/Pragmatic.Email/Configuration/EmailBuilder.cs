using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Email.Middleware;
using Pragmatic.Email.Security;
using Pragmatic.Email.Smtp;
using Pragmatic.Email.Transport;

namespace Pragmatic.Email.Configuration;

/// <summary>
///     Fluent builder for configuring the email system.
/// </summary>
/// <remarks>
///     Naming convention: <c>Use*</c> methods configure the single transport (last call wins),
///     <c>Add*</c> methods accumulate pipeline middleware (all registrations are active).
/// </remarks>
public sealed class EmailBuilder
{
    public IServiceCollection Services { get; }

    internal EmailBuilder(IServiceCollection services) => Services = services;

    /// <summary>Uses SMTP transport with connection pooling.</summary>
    public EmailBuilder UseSmtp(Action<SmtpTransportOptions> configure)
    {
        Services.Configure(configure);
        ReplaceTransport<SmtpTransport>();
        return this;
    }

    /// <summary>Uses the null transport (no-op, for testing).</summary>
    public EmailBuilder UseNullTransport()
    {
        ReplaceTransport<NullTransport>();
        return this;
    }

    /// <summary>Registers a custom transport.</summary>
    public EmailBuilder UseTransport<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TTransport>()
        where TTransport : class, IEmailTransport
    {
        ReplaceTransport<TTransport>();
        return this;
    }

    /// <summary>
    ///     Removes any previously registered <see cref="IEmailTransport"/> singleton and registers
    ///     <typeparamref name="TTransport"/> instead. Calling <c>UseSmtp</c>/<c>UseNullTransport</c>/
    ///     <c>UseTransport</c> multiple times is a misconfiguration; this ensures last-call wins
    ///     without silently accumulating wasted singleton registrations.
    /// </summary>
    private void ReplaceTransport<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TTransport>()
        where TTransport : class, IEmailTransport
    {
        // Remove all existing IEmailTransport registrations to prevent duplicates.
        var existing = Services
            .Where(d => d.ServiceType == typeof(IEmailTransport))
            .ToList();
        foreach (var descriptor in existing)
            Services.Remove(descriptor);

        Services.AddSingleton<IEmailTransport, TTransport>();
    }

    /// <summary>Adds a middleware to the email pipeline.</summary>
    public EmailBuilder AddMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TMiddleware>()
        where TMiddleware : class, IEmailMiddleware
    {
        Services.AddSingleton<IEmailMiddleware, TMiddleware>();
        return this;
    }

    /// <summary>Enables DKIM signing on outgoing emails.</summary>
    public EmailBuilder EnableDkim(Action<DkimOptions> configure)
    {
        Services.Configure(configure);
        Services.AddSingleton<IEmailMiddleware, DkimMiddleware>();
        return this;
    }

    /// <summary>Enables S/MIME signing on outgoing emails.</summary>
    public EmailBuilder EnableSmime(Action<SmimeOptions> configure)
    {
        Services.Configure(configure);
        Services.AddSingleton<IEmailMiddleware, SmimeMiddleware>();
        return this;
    }

    /// <summary>Configures global email options.</summary>
    public EmailBuilder Configure(Action<EmailOptions> configure)
    {
        Services.Configure(configure);
        return this;
    }
}
