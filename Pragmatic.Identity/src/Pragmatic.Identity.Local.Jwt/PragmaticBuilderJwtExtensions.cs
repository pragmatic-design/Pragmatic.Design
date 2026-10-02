using Pragmatic.Abstractions.Http;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Pragmatic.Composition;
using Pragmatic.Composition.Abstractions;
using Pragmatic.Composition.Steps;
using Pragmatic.Identity.Authorization;
using Pragmatic.Identity.Local.Services;

namespace Pragmatic.Identity.Local.Jwt;

/// <summary>
///     Extension methods for configuring JWT authentication on <see cref="IPragmaticBuilder" />.
/// </summary>
public static class PragmaticBuilderJwtExtensions
{
    /// <summary>
    ///     Configures JWT Bearer authentication from a configuration section — <c>Jwt</c> unless named:
    ///     <c>Key</c> (required), <c>Issuer</c>, <c>Audience</c>, and optionally <c>TokenExpiration</c>,
    ///     <c>ClockSkew</c> (TimeSpan, e.g. <c>00:20:00</c>) and <c>RequireSecurityStamp</c>.
    /// </summary>
    /// <remarks>
    ///     The same registration as <see cref="UseJwtAuthentication(IPragmaticBuilder, Action{JwtOptions})" />,
    ///     with the same checks. Every host that used JWT wrote this mapping by hand, from the same keys.
    ///     Read key by key rather than bound: the signing key is <c>Key</c> in the section, and a binder would
    ///     look for <c>SigningKey</c>.
    /// </remarks>
    /// <example>
    ///     <code>
    ///     app.UseJwtAuthentication();   // Jwt:Key, Jwt:Issuer, Jwt:Audience
    ///     </code>
    /// </example>
    /// <exception cref="InvalidOperationException">
    ///     <c>{section}:Key</c> is absent, or a value is not of its type — named in the message.
    /// </exception>
    public static IPragmaticBuilder UseJwtAuthentication(this IPragmaticBuilder builder, string section = "Jwt")
    {
        ArgumentNullException.ThrowIfNull(builder);

        var settings = builder.Configuration.GetSection(section);
        var key = settings["Key"]
                  ?? throw new InvalidOperationException($"{section}:Key is not configured.");
        var expiration = TimeSpanOf(settings, section, "TokenExpiration");
        var skew = TimeSpanOf(settings, section, "ClockSkew");
        var requireStamp = BooleanOf(settings, section, "RequireSecurityStamp");

        return builder.UseJwtAuthentication(jwt =>
        {
            jwt.SigningKey = key;
            jwt.Issuer = settings["Issuer"];
            jwt.Audience = settings["Audience"];
            if (expiration is { } e) jwt.TokenExpiration = e;
            if (skew is { } s) jwt.ClockSkew = s;
            if (requireStamp is { } r) jwt.RequireSecurityStamp = r;
        });
    }

    private static TimeSpan? TimeSpanOf(IConfigurationSection settings, string section, string name)
        => settings[name] is not { } value
            ? null
            : TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw new InvalidOperationException($"{section}:{name} is '{value}', not a TimeSpan (e.g. 00:20:00).");

    private static bool? BooleanOf(IConfigurationSection settings, string section, string name)
        => settings[name] is not { } value
            ? null
            : bool.TryParse(value, out var parsed)
                ? parsed
                : throw new InvalidOperationException($"{section}:{name} is '{value}', not true or false.");

    /// <summary>
    ///     Configures JWT Bearer authentication with the Pragmatic authorization pipeline.
    ///     Registers <see cref="JwtTokenGenerator" /> for token creation — as the
    ///     <see cref="IAccessTokenIssuer" /> the package's <c>SignInUser</c> signs with — and the
    ///     ASP.NET Core JwtBearer handler for token validation.
    /// </summary>
    /// <example>
    ///     <code>
    ///     app.UseJwtAuthentication(jwt =>
    ///     {
    ///         jwt.SigningKey = builder.Configuration["Jwt:Key"]!;
    ///         jwt.Issuer = "https://myapp.example.com";
    ///         jwt.TokenExpiration = TimeSpan.FromHours(1);
    ///     });
    ///     </code>
    /// </example>
    public static IPragmaticBuilder UseJwtAuthentication(
        this IPragmaticBuilder builder,
        Action<JwtOptions> configure)
    {
        var options = new JwtOptions { SigningKey = "" };
        configure(options);

        if (string.IsNullOrWhiteSpace(options.SigningKey))
            throw new ArgumentException("JWT SigningKey must be configured.", nameof(options.SigningKey));

        // HMAC-SHA256 requires a key of at least 256 bits. A shorter key is accepted by
        // SymmetricSecurityKey but materially weakens the signature — reject it up front. The remaining
        // safety invariants (entropy, expiry, skew, and — critically — the production issuer/audience
        // requirement) are enforced by JwtOptionsValidator below so they cannot be bypassed by binding
        // JwtOptions through a different configuration path.
        if (Encoding.UTF8.GetByteCount(options.SigningKey) < 32)
            throw new ArgumentException(
                "JWT SigningKey must be at least 32 bytes (256 bits) for HMAC-SHA256.",
                nameof(options.SigningKey));

        // Secure-by-default (#ID-JWT4): every environment is production-strict (HTTPS metadata, issuer and
        // audience required) UNLESS it is explicitly Development. The previous allow-list matched only
        // "Production"/"Prod", so any other name ("Staging", "Live", "Prod-EU") silently downgraded security;
        // matching the single well-known dev name means an unknown name fails closed.
        //
        // The environment is the host's own, the one IHostEnvironment reports to every other component —
        // not ASPNETCORE_/DOTNET_ENVIRONMENT, which would leave a host made Development any other way
        // (WebApplicationFactory.UseEnvironment, --environment) strict here and lenient in
        // HeaderUserMiddleware. A host that sets nothing is "Production", so it stays strict.
        var productionStrict = !builder.Environment.IsDevelopment();

        // Bind options and enforce them at startup via IValidateOptions<JwtOptions> (ValidateOnStart).
        // The validator captures the production-strict flag so the issuer/audience requirement runs for
        // ANY registered JwtOptions, not only those flowing through this extension.
        builder.Services.AddSingleton<IValidateOptions<JwtOptions>>(_ => new JwtOptionsValidator(productionStrict));
        builder.Services.AddOptions<JwtOptions>()
            .Configure(configure)
            .ValidateOnStart();
        builder.Services.AddSingleton<JwtTokenGenerator>();
        // What SignInUser — and any module — signs its tokens with: the contract, so the module's
        // generator does not have to see this registration.
        builder.Services.AddSingleton<IAccessTokenIssuer>(sp => sp.GetRequiredService<JwtTokenGenerator>());

        // ICurrentUser reads the name where the token carries it. JwtBearer renames sub and role to the
        // WS-* URIs IdentityOptions defaults to, which is why ids and permissions always worked; it
        // leaves name alone, so the default left DisplayName null for every bearer caller.
        // Configure, not an assignment: an application that names another claim registers its own
        // Configure after this one, and wins.
        builder.Services.Configure<IdentityOptions>(o => o.DisplayNameClaimType = JwtTokenGenerator.NameClaimType);

        builder.Services.AddAuthentication(authOptions =>
        {
            authOptions.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            authOptions.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer(jwt =>
        {
            jwt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
                ValidateIssuer = options.Issuer is not null,
                ValidIssuer = options.Issuer,
                ValidateAudience = options.Audience is not null,
                ValidAudience = options.Audience,
                ValidateLifetime = true,
                ClockSkew = options.ClockSkew,
                NameClaimType = JwtTokenGenerator.NameClaimType,

                // The name the role claims arrive under, not the one they were written with: JwtBearer
                // has already renamed "role" to ClaimTypes.Role, and "role" here left IsInRole and
                // [Authorize(Roles = …)] finding no roles at all. Keycloak and OIDC already say this.
                RoleClaimType = ClaimTypes.Role
            };
            jwt.RequireHttpsMetadata = productionStrict;

            // Security-stamp revocation: on every validated token re-load the identity and compare its
            // current stamp against the token's "sstamp" claim. A mismatch means the stamp was rotated
            // (password change/reset) after the token was issued, so the token must be rejected.
            jwt.Events = new JwtBearerEvents
            {
                OnTokenValidated = static async context =>
                {
                    var tokenStamp = context.Principal?.FindFirst("sstamp")?.Value;

                    if (string.IsNullOrEmpty(tokenStamp))
                    {
                        // A stamp-less token can only be honoured during an explicit migration window.
                        // Fail closed by default (RequireSecurityStamp = true): a token with no "sstamp"
                        // cannot be revoked, so accepting it survives password changes/resets.
                        var jwtOptions = context.HttpContext.RequestServices
                            .GetService<IOptions<JwtOptions>>()?.Value;
                        if (jwtOptions is null || jwtOptions.RequireSecurityStamp)
                        {
                            context.Fail("Token has no security stamp and RequireSecurityStamp is enabled.");
                            return;
                        }

                        // Lenient migration path: accept the legacy stamp-less token.
                        return;
                    }

                    // The account is found by the key the token carries, as IAuthenticationContext finds
                    // it: a subject is free to be a pseudonym, so that nothing recording who acted
                    // records an email, and looking the account up by "sub" refused every such token.
                    // A token without the key names the account in "sub" — which JwtBearer
                    // may map to the legacy NameIdentifier URI, so both are checked.
                    var subject = context.Principal?.FindFirst(ExternalIdentityKey.ClaimType)?.Value
                                  ?? context.Principal?.FindFirst("sub")?.Value
                                  ?? context.Principal?.FindFirst(
                                      "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
                    if (string.IsNullOrEmpty(subject))
                    {
                        context.Fail("Token has a security stamp but no subject to validate it against.");
                        return;
                    }

                    // ILocalIdentityStore is typically scoped (DB-backed); resolve it per-request.
                    var store = context.HttpContext.RequestServices.GetService<ILocalIdentityStore>();
                    if (store is null)
                    {
                        // Stamp validation was requested (claim present) but no store is wired — fail closed.
                        context.Fail("Cannot validate token security stamp: no ILocalIdentityStore registered.");
                        return;
                    }

                    var identity = await store
                        .FindByExternalKeyAsync(subject, context.HttpContext.RequestAborted)
                        .ConfigureAwait(false);

                    if (identity is null || !identity.IsActive
                        || !string.Equals(identity.SecurityStamp, tokenStamp, StringComparison.Ordinal))
                    {
                        context.Fail("Token security stamp is no longer valid.");
                    }
                }
            };
        });

        builder.Services.AddAuthorization();
        builder.Services.AddPragmaticAuthorization();

        // The sign-in endpoints this package's tokens are issued from, capped per client address.
        LoginRateLimit.Register(builder.Services, builder.Configuration);

        // Without this the middleware is never added and every endpoint carrying authorization
        // metadata answers 500. The step is guarded, so registering it twice is harmless.
        builder.Services.AddSingleton<IStartupStep, AuthenticationStep>();

        // What a caller puts on the wire, said by the thing that decided it. The document
        // generator cannot know which entry point an application calls — the choice is made
        // here, at runtime — and a list of the known ones inside the generator would age in
        // silence: one more arrives, nobody edits the list, the contract is wrong.
        builder.DescribeSecurityScheme(
            global::Pragmatic.Abstractions.Http.OpenApiSecurityScheme.Bearer(
                description: "JWT issued by this application."));

        // What a caller puts on the wire, said by the thing that decided it. The document
        // generator cannot know which entry point an application calls — the choice is made
        // here, at runtime — and a list of the known ones inside the generator would age in
        // silence: one more arrives, nobody edits the list, the contract is wrong.
        builder.Services.AddSingleton<IStartupStep, AuthorizationStep>();

        return builder;
    }
}
