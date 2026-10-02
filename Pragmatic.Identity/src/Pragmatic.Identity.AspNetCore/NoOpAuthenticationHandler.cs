using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pragmatic.Identity;

/// <summary>
///     A no-op authentication handler for development that honors identities
///     set by earlier middleware (e.g. <see cref="HeaderUserMiddleware"/>).
///     When no identity is present, returns "no result" (unauthenticated).
/// </summary>
/// <remarks>
///     This handler is intended for development and integration testing only.
///     It will throw <see cref="InvalidOperationException"/> if used outside of the Development environment.
/// </remarks>
public sealed class NoOpAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IHostEnvironment environment)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!environment.IsDevelopment())
            throw new InvalidOperationException(
                $"{nameof(NoOpAuthenticationHandler)} is a development-only authentication handler and must not be used in the '{environment.EnvironmentName}' environment. " +
                "Replace it with a real authentication scheme before deploying to production.");

        // Honor identity set by HeaderUserMiddleware (or similar dev middleware)
        if (Context.User.Identity?.IsAuthenticated == true)
        {
            var ticket = new AuthenticationTicket(Context.User, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        return Task.FromResult(AuthenticateResult.NoResult());
    }
}
