using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Composition.Abstractions;

namespace Pragmatic.Composition.Steps;

/// <summary>
///     Adds the authentication middleware. Authorization is <see cref="AuthorizationStep" />, at 94,
///     because it has to run after tenant resolution (92) while this one has to run before it.
///     Order 91 — after routing (50) and before <c>InternationalizationStep</c> (93), which resolves
///     the culture from the authenticated user and therefore has to run later.
/// </summary>
/// <remarks>
///     <para>
///         Without this step, every generated endpoint carrying authorization metadata answers 500 —
///         <i>"contains authorization metadata, but a middleware was not found that supports
///         authorization"</i> — unless the consumer writes the step themselves.
///     </para>
///     <para>
///         Guarded rather than unconditional: an application that never called
///         <c>AddAuthentication()</c> must not get the middleware, or it trades one crash for
///         another. The guard reads DI, not reflection — <see cref="IAuthenticationSchemeProvider" />
///         exists only once an authentication stack is registered.
///     </para>
/// </remarks>
public sealed class AuthenticationStep : IStartupStep
{
    /// <inheritdoc />
    public int Order => 91;

    /// <inheritdoc />
    public void ConfigurePipeline(IApplicationBuilder app)
    {
        if (app.ApplicationServices.GetService<IAuthenticationSchemeProvider>() is null)
            return;

        app.UseAuthentication();
    }
}
