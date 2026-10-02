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

namespace Pragmatic.Identity.Oidc;

/// <summary>
///     Configures generic OIDC bearer authentication on <see cref="IPragmaticBuilder"/>: validates tokens from
///     an external OpenID Connect provider via its discovery document and maps the provider's role claim into
///     the Pragmatic authorization pipeline.
/// </summary>
public static class PragmaticBuilderOidcExtensions
{
    /// <example>
    ///     <code>
    ///     app.UseOidcAuthentication(o =>
    ///     {
    ///         o.Authority = "https://idp.example.com/realms/myrealm";
    ///         o.Audience  = "my-api";
    ///         o.RoleClaim = "roles";
    ///     });
    ///     </code>
    /// </example>
    public static IPragmaticBuilder UseOidcAuthentication(
        this IPragmaticBuilder builder,
        Action<OidcOptions> configure)
    {
        var options = new OidcOptions();
        configure(options);

        if (string.IsNullOrWhiteSpace(options.Authority))
            throw new ArgumentException("OIDC Authority must be configured.", nameof(configure));

        // #ID-K3: ValidateAudience is tied to whether Audience is set, so an unset Audience silently accepts
        // tokens minted for ANY audience — including tokens from a different client of the same IdP. Require
        // it outside Development (secure-by-default, mirroring the JWT provider), lenient in Development. The
        // environment is the host's own; a host that sets none is "Production", so it stays strict.
        if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(options.Audience))
            throw new InvalidOperationException(
                "OIDC Audience must be configured outside Development. Leaving it empty disables " +
                "ValidateAudience and accepts tokens issued for any audience.");

        builder.Services.AddOptions<OidcOptions>().Configure(configure).ValidateOnStart();

        // NameClaim names the caller for Pragmatic as well as for ASP.NET Core. Setting only the
        // validator's NameClaimType is not enough: IdentityOptions defaults to the WS-* name URI, which
        // JwtBearer never produces from the provider's name claim, so User.Identity.Name would follow
        // the option and ICurrentUser.DisplayName would stay null. Configure, so an application that
        // wants another claim registers its own after this one and wins.
        builder.Services.Configure<IdentityOptions>(o => o.DisplayNameClaimType = options.NameClaim);

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
                NameClaimType = options.NameClaim,
                RoleClaimType = ClaimTypes.Role
            };
        });

        // Translate the IdP's role claim into ClaimTypes.Role for the Pragmatic authorization pipeline.
        builder.Services.AddTransient<IClaimsTransformation, OidcRoleClaimsTransformer>();

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
                description: "JWT issued by the OpenID Connect provider."));
        builder.Services.AddSingleton<IStartupStep, AuthorizationStep>();

        return builder;
    }
}
