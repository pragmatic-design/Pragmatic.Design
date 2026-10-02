using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Abstractions.Http;
using Pragmatic.Composition;
using Pragmatic.Composition.Abstractions;
using Pragmatic.Composition.Steps;
using Pragmatic.Identity.Authorization;

namespace Pragmatic.Identity;

/// <summary>
///     Extension methods for configuring identity and authentication on <see cref="IPragmaticBuilder" />.
/// </summary>
public static class PragmaticBuilderIdentityExtensions
{
    /// <summary>The scheme name <see cref="NoOpAuthenticationHandler" /> is registered under.</summary>
    public const string DevelopmentScheme = "PragmaticDevelopment";

    /// <param name="builder">The Pragmatic builder.</param>
    extension(IPragmaticBuilder builder)
    {
        /// <summary>
        ///     Development identity from <c>X-User-*</c> headers, in one call.
        /// </summary>
        /// <returns>The builder for chaining.</returns>
        /// <remarks>
        ///     <para>
        ///         Registers the no-op scheme, the authentication pipeline step and
        ///         <see cref="DevelopmentIdentityStep" />, which puts <c>HeaderUserMiddleware</c> ahead
        ///         of the authentication middleware. Outside Development the handler throws, so a
        ///         production host must configure a real scheme instead.
        ///     </para>
        ///     <para>
        ///         The three pieces existed and had to be assembled by hand: the Showcase and two
        ///         consumer projects each wrote the same step, in the same order, for the same reason.
        ///         Three independent copies of one wiring is a missing feature, not a local choice.
        ///     </para>
        /// </remarks>
        public IPragmaticBuilder UseDevelopmentIdentity()
        {
            builder.UseAuthentication<NoOpAuthenticationHandler>(DevelopmentScheme);
            builder.Services.AddSingleton<IStartupStep, DevelopmentIdentityStep>();

            // ⚠️ Not bearer, and this is the case that made the generator stop guessing. The handler
            // reads no credential of its own: it honours whatever HeaderUserMiddleware put on the
            // context, and that middleware reads X-User-Id. Publishing "bearer" here would have
            // described a token nobody checks, and sent whoever tried the API in development looking
            // for a login endpoint that does not exist.
            builder.DescribeSecurityScheme(new OpenApiSecurityScheme
            {
                Name = "developmentUser",
                Type = "apiKey",
                ParameterName = "X-User-Id",
                In = "header",
                Description =
                    "Development only. The user is taken from X-User-Id; X-User-Name, X-User-Roles "
                    + "and X-User-Permissions fill in the rest. Outside Development the handler throws.",
            });

            return builder;
        }

        /// <summary>
        ///     Configures authentication with a custom scheme and handler setup.
        /// </summary>
        /// <param name="configure">Action to configure the authentication builder.</param>
        /// <returns>The builder for chaining.</returns>
        public IPragmaticBuilder UseAuthentication(Action<AuthenticationBuilder> configure)
        {
            var authBuilder = builder.Services.AddAuthentication();
            configure(authBuilder);
            builder.Services.AddAuthorization();
            builder.Services.AddPragmaticAuthorization();

            // Same as the JWT/Keycloak/OIDC entry points: without the step the middleware is never
            // added and every endpoint carrying authorization metadata answers 500. The step is
            // guarded, so registering it twice is harmless.
            builder.Services.AddSingleton<IStartupStep, AuthenticationStep>();
            builder.Services.AddSingleton<IStartupStep, AuthorizationStep>();
            return builder;
        }

        /// <summary>
        ///     Configures authentication with a default scheme and handler.
        /// </summary>
        /// <param name="defaultScheme">The default authentication scheme name.</param>
        /// <param name="configureScheme">Action to configure the authentication scheme options.</param>
        /// <returns>The builder for chaining.</returns>
        public IPragmaticBuilder UseAuthentication<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(string defaultScheme,
            Action<AuthenticationSchemeOptions>? configureScheme = null)
            where THandler : AuthenticationHandler<AuthenticationSchemeOptions>
        {
            builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = defaultScheme;
                options.DefaultChallengeScheme = defaultScheme;
            }).AddScheme<AuthenticationSchemeOptions, THandler>(defaultScheme, configureScheme ?? (_ => { }));

            builder.Services.AddAuthorization();
            builder.Services.AddPragmaticAuthorization();

            // Same as the JWT/Keycloak/OIDC entry points: without the step the middleware is never
            // added and every endpoint carrying authorization metadata answers 500. The step is
            // guarded, so registering it twice is harmless.
            builder.Services.AddSingleton<IStartupStep, AuthenticationStep>();
            builder.Services.AddSingleton<IStartupStep, AuthorizationStep>();
            return builder;
        }
    }
}
