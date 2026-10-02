using Pragmatic.Abstractions.Http;
using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Pragmatic.Composition;
using Pragmatic.Composition.Abstractions;
using Pragmatic.Composition.Steps;
using Pragmatic.Identity.Authorization;

namespace Pragmatic.Identity.Keycloak;

/// <summary>
///     Configures Keycloak authentication on <see cref="IPragmaticBuilder"/>: bearer validation against the
///     realm's discovery document, <c>realm_access</c> role mapping, and (when a confidential client is
///     supplied) an <see cref="IKeycloakAdminClient"/> for provisioning and role synchronisation.
/// </summary>
public static class PragmaticBuilderKeycloakExtensions
{
    /// <summary>The claim a Keycloak token names its user by, for the validator and for ICurrentUser alike.</summary>
    private const string NameClaimType = "preferred_username";

    /// <example>
    ///     <code>
    ///     app.UseKeycloakAuthentication(k =>
    ///     {
    ///         k.BaseUrl = "https://keycloak.example.com";
    ///         k.Realm   = "myrealm";
    ///         k.Audience = "my-api";
    ///         k.ClientId = "my-api"; k.ClientSecret = secret;   // enables the admin client
    ///     });
    ///     </code>
    /// </example>
    public static IPragmaticBuilder UseKeycloakAuthentication(
        this IPragmaticBuilder builder,
        Action<KeycloakOptions> configure)
    {
        var options = new KeycloakOptions();
        configure(options);

        if (string.IsNullOrWhiteSpace(options.BaseUrl) || string.IsNullOrWhiteSpace(options.Realm))
            throw new ArgumentException("Keycloak BaseUrl and Realm must be configured.", nameof(configure));

        // #ID-K3: ValidateAudience is tied to whether Audience is set, so an unset Audience silently accepts
        // tokens minted for ANY audience — including tokens from another client of the same realm. Require it
        // outside Development (secure-by-default, mirroring the JWT provider), lenient in Development. The
        // environment is the host's own; a host that sets none is "Production", so it stays strict.
        if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(options.Audience))
            throw new InvalidOperationException(
                "Keycloak Audience must be configured outside Development. Leaving it empty disables " +
                "ValidateAudience and accepts tokens issued for any audience.");

        builder.Services.AddOptions<KeycloakOptions>().Configure(configure).ValidateOnStart();

        // ICurrentUser.DisplayName names the caller the way User.Identity.Name does. The validator
        // below reads the name from preferred_username, and IdentityOptions defaulted to the WS-* name
        // URI, which JwtBearer never produces from a Keycloak token: ASP.NET Core called the caller by
        // their username and Pragmatic called them nothing. Configure, so an application
        // that wants another claim registers its own after this one and wins.
        builder.Services.Configure<IdentityOptions>(o => o.DisplayNameClaimType = NameClaimType);

        builder.Services.AddAuthentication(authOptions =>
        {
            authOptions.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            authOptions.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer(jwt =>
        {
            jwt.Authority = options.Authority;
            jwt.Audience = options.Audience;
            jwt.RequireHttpsMetadata = options.RequireHttpsMetadata;
            jwt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = options.Authority,
                ValidateAudience = options.Audience is not null,
                ValidAudience = options.Audience,
                ValidateLifetime = true,
                NameClaimType = NameClaimType,
                RoleClaimType = ClaimTypes.Role
            };
        });

        builder.Services.AddTransient<IClaimsTransformation, KeycloakRoleClaimsTransformer>();

        // Admin/provisioning client — only when a confidential client is configured.
        if (!string.IsNullOrWhiteSpace(options.ClientId) && !string.IsNullOrWhiteSpace(options.ClientSecret))
            builder.Services.AddHttpClient<IKeycloakAdminClient, KeycloakAdminClient>();

        builder.Services.AddAuthorization();
        builder.Services.AddPragmaticAuthorization();

        // Without this the middleware is never added and every endpoint carrying authorization
        // metadata answers 500. The step is guarded, so registering it twice is harmless.
        builder.Services.AddSingleton<IStartupStep, AuthenticationStep>();

        // What a caller puts on the wire, said by the thing that decided it. The document
        // generator cannot know which entry point an application calls — the choice is made
        // here, at runtime — and a list of the known ones inside the generator would age in
        // silence: one more arrives, nobody edits the list, the contract is wrong.
        builder.DescribeSecurityScheme(
            global::Pragmatic.Abstractions.Http.OpenApiSecurityScheme.Bearer(
                description: "JWT issued by Keycloak."));
        builder.Services.AddSingleton<IStartupStep, AuthorizationStep>();

        return builder;
    }
}
